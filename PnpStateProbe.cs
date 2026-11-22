using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace USBGuardian.App.Services
{
    internal enum DeviceEnableState { Unknown, Enabled, Disabled }
    internal sealed class PnpState
    {
        public bool Found { get; init; }
        public string Class { get; init; } = "";
        public DeviceEnableState State { get; init; }
    }
    internal static class PnpStateProbe
    {
        // Read only. Disabled = Windows CM_PROB_DISABLED (22).
        public static async Task<PnpState> QueryAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) ||
                id.Length > 512 || id.IndexOfAny(new[] { '\r', '\n', '\'', '"' }) >= 0)
                return new PnpState();
            string literal = id.Replace("'", "''");
            string script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$id = '" + literal + @"'
$d = Get-PnpDevice -InstanceId $id -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $d) {
  $r = [PSCustomObject]@{ Found=$false; Class=''; Problem=-1; Status='' }
} else {
  $problem = -1
  try {
    $p = Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction Stop
    if ($null -ne $p.Data) { $problem = [int]$p.Data }
  } catch {}
  $r = [PSCustomObject]@{ Found=$true; Class=[string]$d.Class; Problem=$problem; Status=[string]$d.Status }
}
ConvertTo-Json -InputObject $r -Compress
";
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = exe, Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }) ?? throw new IOException("PnP status query did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(12));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("PnP status query timed out.");
            }
            string output = await stdout;
            string error = await stderr;
            if (process.ExitCode != 0)
                throw new IOException("PnP status query failed: " + error[..Math.Min(error.Length, 180)]);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement item = document.RootElement;
            bool found = item.GetProperty("Found").GetBoolean();
            string cls = item.GetProperty("Class").GetString() ?? "";
            int problem = item.GetProperty("Problem").GetInt32();
            string status = item.GetProperty("Status").GetString() ?? "";
            return new PnpState
            {
                Found = found,
                Class = cls,
                State = !found ? DeviceEnableState.Unknown :
                    problem == 22 ? DeviceEnableState.Disabled :
                    problem == 0 && status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                        ? DeviceEnableState.Enabled : DeviceEnableState.Unknown
            };
        }
    }
}
