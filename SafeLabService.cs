using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace USBGuardian.App.Services
{
    internal sealed class SafeLabResult
    {
        public bool IsolationVerified { get; set; }
        // A file may be too large for the bounded preview. This is an expected
        // outcome, not a worker failure and not a successful safety scan.
        public bool SkippedDueToSize { get; set; }
        public long FileSizeBytes { get; set; }
        public string FileKind { get; set; } = "";
        public string PreviewMode { get; set; } = "none";
        public string Hex { get; set; } = "";
        public string Text { get; set; } = "";
        public int ByteCount { get; set; }
        public string Sha256 { get; set; } = "";
        // Sanitized raw BGRA pixels produced by the isolated worker, never an encoded image.
        public int PreviewWidth { get; set; }
        public int PreviewHeight { get; set; }
        public string PreviewPixelsBase64 { get; set; } = "";
        public string PreviewNote { get; set; } = "";
    }

    internal static class SafeLabService
    {
        private const long MaxFileSize = 24L * 1024 * 1024;
        private const long MaxEncodedImage = 8L * 1024 * 1024;
        public static Task<SafeLabResult> AnalyzeAsync(string filePath) =>
            Task.Run(() => Analyze(filePath));

        private static SafeLabResult Analyze(string path)
        {
            // The host reads only the selected file and passes up to 2 KiB
            // to an AppContainer worker; the worker receives NO USB path.
            FileInfo metadata = new(path);
            if ((metadata.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("不接受檔案連結或重新剖析點。");
            if (metadata.Length > MaxFileSize)
                return TooLarge(metadata.Length);

            byte[] head;
            string hash;
            using (FileStream source = new(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 8192, FileOptions.SequentialScan))
            {
                // Re-check the opened stream: the USB file could have changed
                // between metadata inspection and opening it.
                if (source.Length > MaxFileSize)
                    return TooLarge(source.Length);
                head = new byte[(int)Math.Min(2048L, source.Length)];
                int loaded = 0;
                while (loaded < head.Length)
                {
                    int n = source.Read(head, loaded, head.Length - loaded);
                    if (n == 0) break;
                    loaded += n;
                }
                if (loaded != head.Length) Array.Resize(ref head, loaded);
                source.Position = 0;
                using SHA256 sha = SHA256.Create();
                hash = Convert.ToHexString(sha.ComputeHash(source));
            }

            string analyzer = Path.Combine(AppContext.BaseDirectory, "SafeLab", "USBGuardian.Analyzer.exe");
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "USBGuardian", "SafeLabResults");
            Directory.CreateDirectory(directory);
            CleanupOldTemporaryFiles(directory);
            string token = Guid.NewGuid().ToString("N");
            string output = Path.Combine(directory, token + ".json");
            string? previewInput = null;
            try
            {
                File.WriteAllText(output, "", new UTF8Encoding(false));
                // Only stage image formats recognized by signature; the worker alone decodes them.
                // Limit the compressed file size to avoid expensive background work.
                bool isImage = head.Length >= 4 && (
                    (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) ||
                    (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) ||
                    (head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38) ||
                    (head[0] == 0x42 && head[1] == 0x4D));
                if (isImage && metadata.Length > 0 && metadata.Length <= MaxEncodedImage)
                {
                    previewInput = Path.Combine(directory, token + ".input");
                    // Host copies only this selected file; worker does not get USB source path.
                    using FileStream copySource = new(path, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 8192, FileOptions.SequentialScan);
                    using FileStream copyTarget = new(previewInput, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 8192, FileOptions.SequentialScan);
                    copySource.CopyTo(copyTarget);
                    if (copyTarget.Length > MaxEncodedImage)
                        throw new InvalidDataException("預覽檔案超過 8 MB 限制");
                }
                AppContainerLauncher.Run(analyzer, output, head, previewInput);
                FileInfo resultFile = new(output);
                if (!resultFile.Exists || resultFile.Length == 0 || resultFile.Length > 7_000_000)
                    throw new InvalidDataException("隔離程序未產生有效結果。");
                SafeLabResult? result = JsonSerializer.Deserialize<SafeLabResult>(File.ReadAllText(output));
                if (result == null || !result.IsolationVerified)
                    throw new InvalidDataException("AppContainer 權限驗證未通過，拒絕顯示分析結果。");
                if (result.PreviewMode != "text" && result.PreviewMode != "image" && result.PreviewMode != "none")
                    throw new InvalidDataException("分析器傳回未知的預覽模式");
                result.Sha256 = hash;
                if (isImage && metadata.Length > MaxEncodedImage)
                    result.PreviewNote = "圖片超過 8 MB 的預覽上限，仍可查看格式與雜湊資訊。";
                // Reject a malformed pixel payload from any worker, even an isolated one.
                if (result.PreviewWidth != 0 || result.PreviewHeight != 0 ||
                    result.PreviewPixelsBase64.Length != 0)
                {
                    if (result.PreviewWidth < 1 || result.PreviewHeight < 1 ||
                        result.PreviewWidth > 900 || result.PreviewHeight > 700 ||
                        (long)result.PreviewWidth * result.PreviewHeight * 4 > 2_520_000)
                        throw new InvalidDataException("預覽像素尺寸超出上限");
                    byte[] pixels = Convert.FromBase64String(result.PreviewPixelsBase64);
                    if (pixels.LongLength != (long)result.PreviewWidth * result.PreviewHeight * 4)
                        throw new InvalidDataException("預覽像素資料長度錯誤");
                }
                return result;
            }
            finally
            {
                try { File.Delete(output); } catch { }
                if (previewInput != null)
                {
                    try { File.Delete(previewInput); } catch { }
                }
            }
        }
        private static SafeLabResult TooLarge(long length) => new()
        {
            SkippedDueToSize = true,
            FileSizeBytes = length,
            IsolationVerified = false,
            PreviewMode = "none",
            FileKind = "未分析（檔案超過預覽大小限制）"
        };

        private static void CleanupOldTemporaryFiles(string directory)
        {
            // Delete only SafeLab-staged files older than one day.
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string extension = Path.GetExtension(file);
                if (extension != ".input" && extension != ".json") continue;
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                        File.Delete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
