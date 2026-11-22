using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace USBGuardian.App.Services
{
    // Proof-oriented relationship: Win32_LogicalDisk -> partition -> USB disk PnP ID.
    // It intentionally refuses guessing, and will not match fixed-drive USB SSDs here.
    internal static class UsbDriveMapping
    {
        public static async Task<string?> ResolveStorageInstanceAsync(string drive, CancellationToken cancel)
        {
            if (!Regex.IsMatch(drive, @"^[A-Za-z]:\\$")) return null;
            try
            {
                DriveInfo d = new(drive);
                if (!d.IsReady || d.DriveType != DriveType.Removable) return null;
            }
            catch { return null; }
            string letter = drive[..2].ToUpperInvariant(); // validated before PowerShell interpolation
            string script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$target = '" + letter + @"'
$ids = @(
 foreach ($disk in @(Get-CimInstance Win32_DiskDrive | Where-Object { $_.PNPDeviceID -like 'USBSTOR\*' })) {
  foreach ($partition in @(Get-CimAssociatedInstance -InputObject $disk -Association Win32_DiskDriveToDiskPartition)) {
   foreach ($logical in @(Get-CimAssociatedInstance -InputObject $partition -Association Win32_LogicalDiskToPartition)) {
    if ($logical.DeviceID -eq $target) { [string]$disk.PNPDeviceID }
   }
  }
 }
)
ConvertTo-Json -InputObject $ids -Depth 2 -Compress
";
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }) ?? throw new IOException("Unable to query Windows disk association.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("磁碟關聯查詢逾時。 / Drive mapping timed out.");
            }
            string json = await stdout;
            string error = await stderr;
            if (process.ExitCode != 0)
                throw new IOException("磁碟關聯查詢失敗。 / Disk mapping failed: " +
                    error[..Math.Min(200, error.Length)]);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            List<string> ids = document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(x => x.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return ids.Count == 1 ? ids[0] : null;
        }
    }
}
