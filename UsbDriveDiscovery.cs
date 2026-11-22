using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace USBGuardian.App.Services
{
    /// <summary>
    /// Returns only drive letters that Windows Storage reports on USB-bus disks.
    /// This complements DriveType.Removable; an external SSD may be DriveType.Fixed.
    /// Fail closed: if Windows cannot establish USB bus membership, do not add it.
    /// No filesystem content is read and no external commands are downloaded.
    /// </summary>
    internal static class UsbDriveDiscovery
    {
        public static async Task<IReadOnlyCollection<string>> FindUsbBusVolumesAsync(CancellationToken token)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            const string script = @"
$ErrorActionPreference='Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Get-Disk | Where-Object { $_.BusType -eq 'USB' } | ForEach-Object {
    $_ | Get-Partition | Where-Object { $_.DriveLetter } | ForEach-Object {
        [string]$_.DriveLetter + ':\'
    }
}
";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " +
                        Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            try
            {
                if (!process.Start()) return Array.Empty<string>();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(9));
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    token.ThrowIfCancellationRequested();
                    return Array.Empty<string>();
                }
                string output = await stdout;
                _ = await stderr;
                if (process.ExitCode != 0) return Array.Empty<string>();
                return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length == 3 && char.IsLetter(x[0]) && x[1] == ':' && x[2] == '\\')
                    .Select(x => x.ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (System.ComponentModel.Win32Exception) { return Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
        }
    }
}
