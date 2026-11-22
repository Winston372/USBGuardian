using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace USBGuardian.App.Services
{
    internal static class UsageConsent
    {
        internal const string PolicyVersion = "USBGuardian-3.2-2026-11-18";
        internal static string NoticePath => Path.Combine(AppContext.BaseDirectory,
            UiPreferences.Language == "en" ? "FirstUseNotice.en.txt" : "FirstUseNotice.zh-TW.txt");
        private static string StatePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USBGuardian", "usage-consent.json");

        private sealed class ConsentRecord
        {
            public string PolicyVersion { get; set; } = "";
            public string DocumentSha256 { get; set; } = "";
            public string InstallDirectory { get; set; } = "";
            public DateTime AcceptedAtUtc { get; set; }
        }

        internal static string ReadNotice()
        {
            // Fail closed if the packaged notice is missing: never silently skip consent.
            if (!File.Exists(NoticePath))
                throw new FileNotFoundException("找不到使用須知文件，無法啟動監控。請重新安裝完整程式。", NoticePath);
            return File.ReadAllText(NoticePath, Encoding.UTF8);
        }

        private static string CurrentInstallDirectory =>
            Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string Digest(string text) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        internal static bool HasAccepted(string notice)
        {
            try
            {
                if (!File.Exists(StatePath)) return false;
                ConsentRecord? record = JsonSerializer.Deserialize<ConsentRecord>(File.ReadAllText(StatePath));
                return record != null && record.PolicyVersion == PolicyVersion &&
                    string.Equals(record.DocumentSha256, Digest(notice), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.InstallDirectory, CurrentInstallDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        internal static void SaveAcceptance(string notice)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            string temp = StatePath + ".tmp";
            string json = JsonSerializer.Serialize(new ConsentRecord
            {
                PolicyVersion = PolicyVersion,
                DocumentSha256 = Digest(notice),
                InstallDirectory = CurrentInstallDirectory,
                AcceptedAtUtc = DateTime.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json, Encoding.UTF8);
            File.Move(temp, StatePath, overwrite: true);
        }
    }
}
