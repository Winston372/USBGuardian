using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace USBGuardian.App.Services
{
    internal sealed class FileEntry
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsDirectory { get; init; }
        public bool IsReparsePoint { get; init; }
        public bool Hidden { get; init; }
        public bool System { get; init; }
        public string Risk { get; init; } = "";
        public bool HasLoadedChildren { get; set; }
        public override string ToString() =>
            $"{(IsDirectory ? "▸" : "▤")} {Name}{(Risk.Length == 0 ? "" : "  ⚠")}";
    }

    internal sealed class FolderSnapshot
    {
        public List<FileEntry> Entries { get; } = new();
        public bool Truncated { get; set; }
        public string? Error { get; set; }
        public int RiskCount => Entries.Count(x => !string.IsNullOrEmpty(x.Risk));
    }

    internal static class FileInspection
    {
        private static readonly HashSet<string> Executables =
            new(StringComparer.OrdinalIgnoreCase)
            { ".exe", ".scr", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".hta", ".msi", ".lnk" };
        private static readonly HashSet<string> DocumentLike =
            new(StringComparer.OrdinalIgnoreCase)
            { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".txt", ".xlsx", ".mp4" };

        public static FolderSnapshot List(string path, int limit = 240)
        {
            FolderSnapshot result = new();
            try
            {
                foreach (string full in Directory.EnumerateFileSystemEntries(path))
                {
                    if (result.Entries.Count >= limit) { result.Truncated = true; break; }
                    try
                    {
                        FileAttributes attr = File.GetAttributes(full);
                        bool directory = (attr & FileAttributes.Directory) != 0;
                        string name = System.IO.Path.GetFileName(full);
                        result.Entries.Add(new FileEntry
                        {
                            Name = name, Path = full, IsDirectory = directory,
                            Hidden = (attr & FileAttributes.Hidden) != 0,
                            System = (attr & FileAttributes.System) != 0,
                            IsReparsePoint = (attr & FileAttributes.ReparsePoint) != 0,
                            Risk = RiskReason(name, attr)
                        });
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { result.Error = e.Message; }
            return result;
        }

        public static string RiskReason(string name, FileAttributes attributes)
        {
            bool directory = (attributes & FileAttributes.Directory) != 0;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return "連結／重新剖析點：不進入此路徑";
            if (directory) return "";
            string ext = Path.GetExtension(name);
            string previous = Path.GetExtension(Path.GetFileNameWithoutExtension(name));
            if (Executables.Contains(ext) && DocumentLike.Contains(previous))
                return $"疑似誤導性雙重副檔名：實際是 {ext}";
            if (name.IndexOfAny(new[] { '\u202A', '\u202B', '\u202C', '\u202D', '\u202E' }) >= 0)
                return "檔名含雙向文字控制字元，可能誤導副檔名顯示";
            if ((attributes & FileAttributes.Hidden) != 0 && Executables.Contains(ext))
                return "隱藏的程式或腳本類型";
            if (ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".hta", StringComparison.OrdinalIgnoreCase))
                return "可執行的螢幕保護程式或 HTML 應用程式";
            if (Executables.Contains(ext))
                return "程式、腳本或捷徑：請確認來源（不代表有毒）";
            return "";
        }

        public static bool HighAttention(FileEntry item)
        {
            if (item.IsDirectory) return false;
            string r = item.Risk;
            return r.StartsWith("疑似誤導性") ||
                   r.StartsWith("檔名含雙向") ||
                   r.StartsWith("隱藏的") ||
                   r.StartsWith("可執行的螢幕");
        }
    }
}
