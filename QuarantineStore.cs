using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace USBGuardian.App.Services
{
    // This is reversible, encrypted-on-disk quarantine, NOT a kernel malware blocker.
    // Every operation is user-initiated. Malicious content is never executed or decoded here.
    internal sealed class QuarantineRecord
    {
        public QuarantineRecord() { }
        public string Id { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long OriginalLength { get; set; }
        public DateTime StoredUtc { get; set; }
        public bool RemovalConfirmed { get; set; }
        public string ProtectedKey { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public override string ToString() =>
            $"{(RemovalConfirmed ? "" : "[待核對] ")}{FileName}  ·  {StoredUtc.ToLocalTime():yyyy/MM/dd HH:mm}";
    }

    internal static class QuarantineStore
    {
        public const long MaxLength = 24L * 1024 * 1024; // bounded memory and file operations
        private static readonly object Gate = new();
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USBGuardian", "Quarantine");
        private static string Index => Path.Combine(Root, "index.json");

        // Only Windows DPAPI + AES-GCM; no external packages and no plaintext staging file.
        public static QuarantineRecord Quarantine(string path, string reviewedSha256, string selectedDrive)
        {
            lock (Gate)
            {
                string full = Path.GetFullPath(path);
                ValidateRemovableSource(full, selectedDrive);
                if (reviewedSha256.Length != 64 ||
                    !reviewedSha256.All(Uri.IsHexDigit))
                    throw new InvalidOperationException("必須先完成 SafeLab 預覽並取得檔案指紋。 / Preview first.");

                FileInfo before = new(full);
                if (before.Length > MaxLength)
                    throw new IOException("隔離檔案限制為 24 MB。 / Quarantine limit: 24 MB.");
                long size = before.Length;
                DateTime modified = before.LastWriteTimeUtc;
                byte[] plaintext = File.ReadAllBytes(full);
                try
                {
                    if (plaintext.LongLength != size || File.GetLastWriteTimeUtc(full) != modified)
                        throw new IOException("檔案在檢查後已變更，請重新預覽。 / File changed.");
                    string digest = Convert.ToHexString(SHA256.HashData(plaintext));
                    if (!string.Equals(digest, reviewedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("檔案內容與預覽時不同，已取消隔離。 / Hash mismatch.");
                    ValidateRemovableSource(full, selectedDrive);
                    EnsureRoot();
                    var records = LoadIndex();
                    string id = Guid.NewGuid().ToString("N");
                    string blobPath = Path.Combine(Root, id + ".ugd");
                    string temporary = blobPath + ".tmp";
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] nonce = RandomNumberGenerator.GetBytes(12);
                    byte[] tag = new byte[16];
                    byte[] ciphertext = new byte[plaintext.Length];
                    try
                    {
                        using (AesGcm aes = new(key, 16))
                            aes.Encrypt(nonce, plaintext, ciphertext, tag);
                        var record = new QuarantineRecord
                        {
                            Id = id, FileName = Path.GetFileName(full), OriginalPath = full,
                            Sha256 = digest, OriginalLength = size, StoredUtc = DateTime.UtcNow,
                            ProtectedKey = Convert.ToBase64String(Dpapi.Protect(key)),
                            Nonce = Convert.ToBase64String(nonce), Tag = Convert.ToBase64String(tag)
                        };
                        try
                        {
                            File.WriteAllBytes(temporary, ciphertext);
                            File.Move(temporary, blobPath);
                            records.Add(record);
                            SaveIndex(records); // persist recovery metadata BEFORE deleting USB source
                            try
                            {
                                ValidateRemovableSource(full, selectedDrive);
                                if (File.GetLastWriteTimeUtc(full) != modified ||
                                    new FileInfo(full).Length != size)
                                    throw new IOException("原始檔案已變更；已取消隔離。 / File changed.");
                                File.Delete(full);
                                if (File.Exists(full)) throw new IOException("原始檔案仍存在。 / Not removed.");
                                record.RemovalConfirmed = true;
                                try { SaveIndex(records); }
                                catch { record.RemovalConfirmed = false; } // recoverable metadata remains
                            }
                            catch
                            {
                                records.Remove(record);
                                SaveIndex(records);
                                throw;
                            }
                            return record;
                        }
                        catch
                        {
                            // Never erase the original file if encrypted storage failed.
                            // Orphaned encrypted blobs are harmless but may need cleanup.
                            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                            // Keep any encrypted blob if index persistence is uncertain.
                            // An orphaned encrypted blob is safer than losing recoverable data.
                            throw;
                        }
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
        }

        public static List<QuarantineRecord> List()
        {
            lock (Gate) { return LoadIndex().OrderByDescending(x => x.StoredUtc).ToList(); }
        }

        public static void Restore(string id)
        {
            lock (Gate)
            {
                EnsureRoot();
                var records = LoadIndex();
                QuarantineRecord record = GetRecord(records, id);
                string destination = Path.GetFullPath(record.OriginalPath);
                string drive = Path.GetPathRoot(destination) ?? "";
                // Refuse restoring to fixed/system/network drives or a path that now exists.
                if (!IsRemovableRoot(drive))
                    throw new IOException("原始 USB 未連接或非可攜磁碟，無法還原。 / Original removable drive unavailable.");
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("原位置已有同名檔案，未覆寫。 / Destination already exists.");
                string dir = Path.GetDirectoryName(destination) ?? "";
                if (!Directory.Exists(dir) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("原資料夾不存在或包含連結。 / Original folder not available.");
                string encryptedPath = DataPath(record);
                FileInfo stored = new(encryptedPath);
                if (!stored.Exists || stored.Length > MaxLength ||
                    (stored.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("隔離副本不存在、過大或不是一般檔案。 / Invalid quarantine blob.");
                byte[] encrypted = File.ReadAllBytes(encryptedPath);
                byte[] key = Dpapi.Unprotect(Convert.FromBase64String(record.ProtectedKey));
                byte[] plaintext = new byte[encrypted.Length];
                try
                {
                    if (key.Length != 32 || encrypted.LongLength != record.OriginalLength ||
                        encrypted.LongLength > MaxLength)
                        throw new InvalidDataException("隔離檔案大小或金鑰格式錯誤。 / Invalid quarantine file.");
                    using (AesGcm aes = new(key, 16))
                        aes.Decrypt(Convert.FromBase64String(record.Nonce), encrypted,
                            Convert.FromBase64String(record.Tag), plaintext);
                    if (!Convert.ToHexString(SHA256.HashData(plaintext)).Equals(record.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("隔離檔案完整性驗證失敗。 / Integrity failed.");
                    // FileMode.CreateNew guarantees no overwrite. Keep a recoverable copy until index saved.
                    using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write,
                               FileShare.None, 8192, FileOptions.WriteThrough))
                    {
                        output.Write(plaintext);
                        output.Flush(flushToDisk: true);
                    }
                    try
                    {
                        records.Remove(record);
                        SaveIndex(records);
                    }
                    catch
                    {
                        try { File.Delete(destination); } catch { }
                        throw;
                    }
                    try { File.Delete(DataPath(record)); } catch { } // unused orphan is encrypted
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    CryptographicOperations.ZeroMemory(key);
                }
            }
        }

        public static void DeletePermanently(string id)
        {
            lock (Gate)
            {
                var records = LoadIndex();
                QuarantineRecord record = GetRecord(records, id);
                records.Remove(record);
                SaveIndex(records);
                try
                {
                    File.Delete(DataPath(record));
                    if (File.Exists(DataPath(record)))
                        throw new IOException("隔離副本未能刪除。 / Quarantine data still exists.");
                }
                catch
                {
                    // Do not report success if the encrypted file could not be removed.
                    // Re-add the recovery record so the user can retry later.
                    records.Add(record);
                    SaveIndex(records);
                    throw;
                }
            }
        }

        private static QuarantineRecord GetRecord(List<QuarantineRecord> records, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("隔離識別碼無效。 / Invalid quarantine ID.");
            return records.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException("找不到隔離紀錄。 / Quarantine entry missing.");
        }
        private static string DataPath(QuarantineRecord r)
        {
            if (!Guid.TryParseExact(r.Id, "N", out _))
                throw new InvalidDataException("隔離檔案識別碼無效。 / Invalid storage ID.");
            return Path.Combine(Root, r.Id + ".ugd");
        }
        private static void ValidateRemovableSource(string path, string drive)
        {
            string root = Path.GetPathRoot(path) ?? "";
            if (!string.Equals(root, drive, StringComparison.OrdinalIgnoreCase) ||
                !IsRemovableRoot(root) || !File.Exists(path))
                throw new IOException("只能隔離目前所選可攜磁碟的檔案。 / Removable drive only.");
            // Reject junctions in the selected path to avoid writing outside the expected USB tree.
            string? parent = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(parent) &&
                   !string.Equals(parent, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("檔案路徑包含連結資料夾。 / Linked parent directory.");
                string? next = Path.GetDirectoryName(parent);
                if (string.Equals(next, parent, StringComparison.OrdinalIgnoreCase)) break;
                parent = next;
            }
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint |
                               FileAttributes.System | FileAttributes.ReadOnly)) != 0)
                throw new IOException("不處理連結或系統保護檔案。 / Protected or linked file.");
        }
        private static bool IsRemovableRoot(string drive)
        {
            try { DriveInfo d = new(drive); return d.IsReady && d.DriveType == DriveType.Removable; }
            catch { return false; }
        }
        private static void EnsureRoot()
        {
            Directory.CreateDirectory(Root);
            if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("隔離資料夾不得為連結。 / Quarantine directory cannot be a link.");
        }
        private static List<QuarantineRecord> LoadIndex()
        {
            EnsureRoot();
            if (!File.Exists(Index)) return new List<QuarantineRecord>();
            string json = File.ReadAllText(Index);
            var rows = JsonSerializer.Deserialize<List<QuarantineRecord>>(json) ?? new();
            if (rows.Count > 10000) throw new InvalidDataException("隔離清單異常。 / Oversized index.");
            return rows;
        }
        private static void SaveIndex(List<QuarantineRecord> records)
        {
            EnsureRoot();
            string temp = Index + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records, new JsonSerializerOptions
            { WriteIndented = true }), Encoding.UTF8);
            File.Move(temp, Index, overwrite: true);
        }
        // DPAPI protects each random AES key under the current Windows account.
        private static class Dpapi
        {
            [StructLayout(LayoutKind.Sequential)] private struct Blob
            { public int Size; public IntPtr Data; }
            [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CryptProtectData(ref Blob input, string? description,
                IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
            [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CryptUnprotectData(ref Blob input, IntPtr description,
                IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr LocalFree(IntPtr handle);
            public static byte[] Protect(byte[] raw) => Transform(raw, false);
            public static byte[] Unprotect(byte[] raw) => Transform(raw, true);
            private static byte[] Transform(byte[] raw, bool decrypt)
            {
                IntPtr buffer = Marshal.AllocHGlobal(raw.Length);
                Marshal.Copy(raw, 0, buffer, raw.Length);
                Blob input = new() { Size = raw.Length, Data = buffer };
                Blob output = default;
                try
                {
                    bool ok = decrypt
                        ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                            IntPtr.Zero, 1, out output)
                        : CryptProtectData(ref input, "USB Guardian quarantine key",
                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                    if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows DPAPI failed");
                    byte[] data = new byte[output.Size];
                    Marshal.Copy(output.Data, data, 0, data.Length);
                    return data;
                }
                finally
                {
                    // clear the unmanaged AES secret before releasing memory
                    for (int i = 0; i < raw.Length; i++) Marshal.WriteByte(buffer, i, 0);
                    Marshal.FreeHGlobal(buffer);
                    if (output.Data != IntPtr.Zero) LocalFree(output.Data);
                }
            }
        }
    }
}
