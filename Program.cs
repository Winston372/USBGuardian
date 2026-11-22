using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace USBGuardian.Analyzer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Refuse to inspect ANY input unless running inside AppContainer.
            if (!IsAppContainer()) return 77;
            try
            {
                if ((args.Length != 4 && args.Length != 6) ||
                    args[0] != "--out" || args[2] != "--data" ||
                    (args.Length == 6 && args[4] != "--image")) return 2;
                string output = args[1];
                byte[] data = Convert.FromBase64String(args[3]);
                if (data.Length > 2048) return 3;
                PreviewResult preview = args.Length == 6
                    ? DecodePreview(args[5]) : new PreviewResult();
                var result = new
                {
                    IsolationVerified = true,
                    FileKind = DetectKind(data),
                    PreviewMode = DetectPreviewMode(data),
                    ByteCount = data.Length,
                    Hex = FormatHex(data),
                    Text = DetectPreviewMode(data) == "text" ? CleanText(data) : "",
                    PreviewWidth = preview.Width,
                    PreviewHeight = preview.Height,
                    PreviewPixelsBase64 = preview.PixelsBase64,
                    PreviewNote = preview.Note
                };
                // Output is one explicitly pre-created ACL-authorized result file.
                File.WriteAllText(output, JsonSerializer.Serialize(result), new UTF8Encoding(false));
                return 0;
            }
            catch { return 5; }
        }

        // Identify presentation mode by file CONTENT, not extension.
        // Unsafe or unknown formats never fall back to system association handlers.
        private static bool IsImage(byte[] b) =>
            Has(b, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) ||
            Has(b, 0xFF, 0xD8, 0xFF) ||
            Has(b, 0x47, 0x49, 0x46, 0x38) ||
            Has(b, 0x42, 0x4D);

        private static bool Has(byte[] b, params byte[] prefix) =>
            b.Length >= prefix.Length && b.AsSpan(0, prefix.Length).SequenceEqual(prefix);

        private static bool IsKnownNonText(byte[] b) =>
            IsImage(b) || Has(b, 0x4D, 0x5A) ||
            Has(b, 0x25, 0x50, 0x44, 0x46) ||
            Has(b, 0x50, 0x4B, 0x03, 0x04) ||
            Has(b, 0x7F, 0x45, 0x4C, 0x46);

        private static string DetectPreviewMode(byte[] b)
        {
            if (IsImage(b)) return "image";
            if (IsKnownNonText(b)) return "none";
            return IsLikelyText(b) ? "text" : "none";
        }

        private static string DetectKind(byte[] b)
        {
            if (b.Length == 0) return "空白檔案";
            if (Has(b, 0x4D, 0x5A)) return "Windows 程式檔（不提供執行預覽）";
            if (Has(b, 0x25, 0x50, 0x44, 0x46)) return "PDF 文件（僅提供格式資訊）";
            if (Has(b, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "PNG 圖片";
            if (Has(b, 0xFF, 0xD8, 0xFF)) return "JPEG 圖片";
            if (Has(b, 0x47, 0x49, 0x46, 0x38)) return "GIF 圖片";
            if (Has(b, 0x42, 0x4D)) return "BMP 圖片";
            if (Has(b, 0x50, 0x4B, 0x03, 0x04)) return "ZIP 或 Office 文件容器（不直接預覽）";
            if (Has(b, 0x7F, 0x45, 0x4C, 0x46)) return "ELF 程式檔";
            if (Has(b, 0x23, 0x21)) return "文字腳本（僅顯示文字，不執行）";
            return IsLikelyText(b) ? "純文字（依內容判定）" : "未知或二進位檔案";
        }

        private static bool IsLikelyText(byte[] data)
        {
            if (data.Length == 0) return false;
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return true; // UTF-16 LE BOM; decoder below skips incomplete terminal code unit.
            if (data.AsSpan().IndexOf((byte)0) >= 0) return false;
            try
            {
                string text = new UTF8Encoding(false, true).GetString(data);
                int valid = 0;
                foreach (char c in text)
                {
                    if (c == '\r' || c == '\n' || c == '\t' ||
                        (!char.IsControl(c) && c != '\uFFFD')) valid++;
                }
                return text.Length > 0 && valid >= text.Length * 0.95;
            }
            catch (DecoderFallbackException)
            {
                // Head chunk may end in the middle of a UTF-8 sequence.
                // Skip at most three terminal bytes and retry.
                for (int trim = 1; trim <= 3 && trim < data.Length; trim++)
                {
                    try
                    {
                        string text = new UTF8Encoding(false, true).GetString(data, 0, data.Length - trim);
                        return text.Length > 0 && text.Count(c => !char.IsControl(c) || c == '\r' || c == '\n' || c == '\t') >= text.Length * 0.95;
                    }
                    catch (DecoderFallbackException) { }
                }
                return false;
            }
        }

        // Only this AppContainer worker decodes the untrusted encoded image.
        // The host never gets an encoded image: it receives strictly bounded BGRA pixels.
        private sealed class PreviewResult
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public string PixelsBase64 { get; set; } = "";
            public string Note { get; set; } = "";
        }

        private static PreviewResult DecodePreview(string path)
        {
            PreviewResult result = new();
            try
            {
                FileInfo file = new(path);
                if (!file.Exists || file.Length <= 0 || file.Length > 8L * 1024 * 1024)
                    throw new InvalidDataException("圖片大小超出 8 MB 上限");
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("禁止處理檔案連結");
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                BitmapDecoder decoder = BitmapDecoder.Create(stream,
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                    throw new InvalidDataException("找不到圖片畫格");
                BitmapFrame frame = decoder.Frames[0];
                long area = (long)frame.PixelWidth * frame.PixelHeight;
                if (area <= 0 || area > 20_000_000 ||
                    frame.PixelWidth > 9000 || frame.PixelHeight > 9000)
                    throw new InvalidDataException("圖片像素數超過安全預覽上限");
                double factor = Math.Min(1.0,
                    Math.Min(900.0 / frame.PixelWidth, 700.0 / frame.PixelHeight));
                BitmapSource resized = factor < 1.0
                    ? new TransformedBitmap(frame, new ScaleTransform(factor, factor))
                    : frame;
                BitmapSource formatted = new FormatConvertedBitmap(resized,
                    PixelFormats.Bgra32, null, 0);
                int stride = checked(formatted.PixelWidth * 4);
                byte[] raw = new byte[checked(stride * formatted.PixelHeight)];
                formatted.CopyPixels(raw, stride, 0);
                result.Width = formatted.PixelWidth;
                result.Height = formatted.PixelHeight;
                result.PixelsBase64 = Convert.ToBase64String(raw);
                result.Note = factor < 1.0
                    ? "已由隔離程序縮小圖片，僅顯示第一個畫格。"
                    : "已在隔離程序內解碼，僅顯示第一個畫格。";
            }
            catch (Exception ex)
            {
                result.Note = "圖片預覽不可用：" +
                    (ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            }
            return result;
        }

        private static string FormatHex(byte[] bytes)
        {
            StringBuilder sb = new();
            for (int offset = 0; offset < bytes.Length; offset += 16)
            {
                sb.Append(offset.ToString("X4")).Append(": ");
                for (int i = offset; i < Math.Min(bytes.Length, offset + 16); i++)
                    sb.Append(bytes[i].ToString("X2")).Append(' ');
                sb.AppendLine();
            }
            return sb.ToString();
        }
        private static string CleanText(byte[] bytes)
        {
            string decoded;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                int count = (bytes.Length - 2) & ~1;
                decoded = Encoding.Unicode.GetString(bytes, 2, count);
            }
            else
            {
                // Be tolerant only of a truncated final UTF-8 character.
                decoded = Encoding.UTF8.GetString(bytes);
            }
            StringBuilder safe = new(decoded.Length);
            foreach (char ch in decoded)
            {
                if (ch == '\r' || ch == '\n' || ch == '\t') safe.Append(ch);
                else if (!char.IsControl(ch) &&
                         ch != '\u202A' && ch != '\u202B' && ch != '\u202C' &&
                         ch != '\u202D' && ch != '\u202E') safe.Append(ch);
                else safe.Append('·');
            }
            return safe.ToString();
        }

        private static bool IsAppContainer()
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out IntPtr token)) return false;
            try
            {
                if (!GetTokenInformation(token, 29, out int enabled, sizeof(int), out int read))
                    return false;
                return enabled == 1;
            }
            finally { CloseHandle(token); }
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass,
            out int information, int size, out int returned);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
