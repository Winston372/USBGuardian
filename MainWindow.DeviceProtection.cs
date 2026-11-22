using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using USBGuardian.App.Services;

namespace USBGuardian.App
{
    // OS enforcement is deliberately opt-in, reversible and limited to USBSTOR.
    // An App-level "untrusted" label is NOT an OS device block.
    public partial class MainWindow
    {
        private sealed class UntrustedDeviceRecord
        {
            public UntrustedDeviceRecord() { }
            public string DisplayName { get; set; } = "";
            public DateTime MarkedUtc { get; set; }
        }

        private sealed class DisabledStorageRecord
        {
            public DisabledStorageRecord() { }
            public string InstanceId { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public DateTime RecordedUtc { get; set; }
            public bool VerifiedDisabled { get; set; }
            public override string ToString() => DisplayName + " · " + RecordedUtc.ToLocalTime().ToString("MM/dd HH:mm");
        }

        private readonly Dictionary<string, UntrustedDeviceRecord> _untrustedDevices =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DisabledStorageRecord> _disabledStorageDevices =
            new(StringComparer.OrdinalIgnoreCase);
        private bool _pnpControlBusy;

        private static string ProtectionFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USBGuardian");
        private static string UntrustedPath => Path.Combine(ProtectionFolder, "untrusted-devices.json");
        private static string DisabledPath => Path.Combine(ProtectionFolder, "disabled-usb-storage.json");

        private void LoadDeviceProtectionRecords()
        {
            try
            {
                if (File.Exists(UntrustedPath))
                {
                    var stored = JsonSerializer.Deserialize<Dictionary<string, UntrustedDeviceRecord>>(
                        File.ReadAllText(UntrustedPath));
                    if (stored != null)
                        foreach (var pair in stored) _untrustedDevices[pair.Key] = pair.Value;
                }
                if (File.Exists(DisabledPath))
                {
                    var stored = JsonSerializer.Deserialize<Dictionary<string, DisabledStorageRecord>>(
                        File.ReadAllText(DisabledPath));
                    if (stored != null)
                        foreach (var pair in stored)
                            if (IsStorageInstanceId(pair.Key) && pair.Value != null)
                                _disabledStorageDevices[pair.Key] = pair.Value;
                }
                UpdateDisabledDeviceChoices();
            }
            catch (Exception ex)
            {
                Log("無法讀取本機裝置保護紀錄：" + ex.Message);
            }
        }

        private static bool SaveJson<T>(string path, T value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(value,
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("無法儲存本機紀錄：" + ex.Message,
                    "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void MarkUntrusted_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedManagedDevice() is not DeviceItem item)
            {
                System.Windows.MessageBox.Show("請先在左側選擇裝置。");
                return;
            }
            if (System.Windows.MessageBox.Show(
                UiPreferences.Runtime(
                    "將這台裝置列為需注意？\n\n再次偵測到相同裝置項目時會提醒；不會停用它，也不是病毒判定。",
                    "Mark this device for attention?\n\nYou will be notified when the same device entry is detected again. This does not disable it or detect malware."),
                UiPreferences.Runtime("列為需注意", "Mark for attention"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            string key = Fingerprint(item.InstanceId);
            _untrustedDevices.TryGetValue(key, out UntrustedDeviceRecord? previous);
            _untrustedDevices[key] = new UntrustedDeviceRecord
            {
                DisplayName = item.Name,
                MarkedUtc = DateTime.UtcNow
            };
            if (!SaveJson(UntrustedPath, _untrustedDevices))
            {
                if (previous != null) _untrustedDevices[key] = previous;
                else _untrustedDevices.Remove(key);
                return;
            }
            item.IsUntrusted = true;
            RefreshDeviceFilters();
            UpdateSelectedDeviceProtection(item);
            UpdateActionDeviceContext();
            Log("使用者標記不信任：" + item.Name);
        }

        private void ClearUntrusted_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedManagedDevice() is not DeviceItem item) return;
            string key = Fingerprint(item.InstanceId);
            _untrustedDevices.TryGetValue(key, out UntrustedDeviceRecord? previous);
            _untrustedDevices.Remove(key);
            if (!SaveJson(UntrustedPath, _untrustedDevices))
            {
                if (previous != null) _untrustedDevices[key] = previous;
                return;
            }
            item.IsUntrusted = false;
            RefreshDeviceFilters();
            UpdateSelectedDeviceProtection(item);
            UpdateActionDeviceContext();
            Log("取消不信任標記：" + item.Name);
            if (_disabledStorageDevices.ContainsKey(item.InstanceId))
                System.Windows.MessageBox.Show(
                    "注意：取消本機標記並不會重新啟用 Windows 裝置。\n" +
                    "請使用下方的『重新啟用（管理員）』。", "USB Guardian");
        }

        private void UpdateSelectedDeviceProtection(DeviceItem item)
        {
            // Controls on the Device detail page must describe its own selected item,
            // never a different entry managed on the Device controls page.
            if (DeviceList.SelectedItem is not DeviceItem shown ||
                !string.Equals(shown.InstanceId, item.InstanceId, StringComparison.OrdinalIgnoreCase)) return;
            DeviceRiskText.Text = item.AttentionLabel;
            DeviceRiskText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                item.AttentionLevel >= 1 ? "WarningTextBrush" : "AccentTextBrush");
            DeviceProtectionText.Text = _disabledStorageDevices.TryGetValue(item.InstanceId, out var last)
                ? (last.VerifiedDisabled
                    ? UiPreferences.Runtime("上次已驗證停用；目前狀態請重新查詢。", "Previously verified disabled; refresh to check current status.")
                    : UiPreferences.Runtime("停用狀態未確認。", "Disable status not verified."))
                : item.IsUntrusted
                    ? UiPreferences.Runtime("已列為需注意；裝置尚未停用。", "Marked for attention; not disabled.")
                    : UiPreferences.Runtime("尚未標記。", "Not marked.");
        }

        // Only USBSTOR instance IDs are eligible. Never disable HID, network,
        // root hubs, keyboards, pointing devices or all devices with a VID/PID.
        private static bool IsStorageInstanceId(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.Length <= 512 &&
            id.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) &&
            !id.Any(c => char.IsControl(c) || c == '"');

        private async void DisableUsbStorage_Click(object sender, RoutedEventArgs e)
        {
            if (_pnpControlBusy) return;
            if (SelectedManagedDevice() is not DeviceItem selected)
            {
                System.Windows.MessageBox.Show("請先選擇儲存裝置。");
                return;
            }
            if (!selected.IsUntrusted)
            {
                System.Windows.MessageBox.Show("請先明確按下『標記不信任』，再決定是否要求停用。");
                return;
            }
            if (!IsStorageInstanceId(selected.InstanceId) ||
                !selected.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show(
                    "為避免錯誤停用鍵盤、滑鼠、網路或 USB 控制器，\n" +
                    "此版本只允許停用 Windows 辨識為 USBSTOR\\... 的 DiskDrive 儲存介面。\n" +
                    "目前選取的項目不符合此限制。", "不可執行此操作");
                return;
            }
            // Re-query Windows before asking for elevation: never trust a stale UI row alone.
            try
            {
                PnpState before = await PnpStateProbe.QueryAsync(selected.InstanceId);
                if (!before.Found || !before.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) ||
                    before.State == DeviceEnableState.Disabled)
                {
                    System.Windows.MessageBox.Show("Windows 無法確認此為目前可操作的 USB 儲存介面，或裝置已停用。請重新查詢。",
                        "已取消操作", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("無法驗證裝置身分；為安全起見不執行停用。\n" + ex.Message);
                return;
            }
            if (System.Windows.MessageBox.Show(
                $"確定要要求 Windows 停用以下 USB 儲存介面？\n\n{selected.Name}\n\n" +
                "需通過 Windows 管理員 UAC 確認。停用後可能無法讀寫裝置，" +
                "有時需重新插拔或重啟才能完成。\n" +
                "請先儲存檔案；此操作不會掃描或刪除惡意檔案。\n\n" +
                "本功能是裝置接入後才發出停用命令，並不能保證事先阻擋其所有動作。",
                "確認停用 USB 儲存介面", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            _pnpControlBusy = true;
            UpdateActionDeviceContext();
            try
            {
                int code = await ExecuteElevatedPnPUtilAsync("/disable-device", selected.InstanceId);
                if (code != 0)
                {
                    Log($"Windows 停用命令失敗，結束碼 {code}：{selected.Name}");
                    System.Windows.MessageBox.Show($"Windows 未成功回報停用（結束碼 {code}）。請查看裝置管理員。", "停用未確認");
                    return;
                }
                // Exit code 0 is not sufficient evidence of device enforcement.
                PnpState after = new PnpState();
                try
                {
                    for (int trial = 0; trial < 3; trial++)
                    {
                        if (trial > 0) await Task.Delay(900);
                        after = await PnpStateProbe.QueryAsync(selected.InstanceId);
                        if (after.State == DeviceEnableState.Disabled) break;
                    }
                }
                catch (Exception ex) { Log("裝置停用狀態驗證失敗：" + ex.Message); }
                bool verified = after.State == DeviceEnableState.Disabled;
                _disabledStorageDevices[selected.InstanceId] = new DisabledStorageRecord
                {
                    InstanceId = selected.InstanceId,
                    DisplayName = selected.Name,
                    RecordedUtc = DateTime.UtcNow,
                    VerifiedDisabled = verified
                };
                SaveJson(DisabledPath, _disabledStorageDevices);
                UpdateDisabledDeviceChoices();
                UpdateSelectedDeviceProtection(selected);
                Log(verified ? "Windows 已驗證 USB 儲存介面停用：" + selected.Name :
                    "Windows 停用命令回報成功，但未能確認停用狀態：" + selected.Name);
                System.Windows.MessageBox.Show(verified
                    ? "Windows 已回報此儲存介面的裝置問題碼 22（已停用）。可從本頁重新啟用。"
                    : "停用命令已送出，但目前無法從 Windows 驗證已停用。請查看裝置管理員；紀錄已保留供復原。",
                    verified ? "已驗證停用" : "停用狀態尚未確認");
                _ = RefreshDeviceListAsync();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log("使用者取消 Windows 管理員授權，未停用裝置");
            }
            catch (Exception ex)
            {
                Log("停用 USB 儲存介面失敗：" + ex.Message);
                System.Windows.MessageBox.Show("無法完成停用：" + ex.Message);
            }
            finally { _pnpControlBusy = false; UpdateActionDeviceContext(); }
        }

        private async void EnableUsbStorage_Click(object sender, RoutedEventArgs e)
        {
            if (_pnpControlBusy) return;
            if (DisabledStorageCombo.SelectedItem is not DisabledStorageRecord record) return;
            if (!IsStorageInstanceId(record.InstanceId)) return;
            if (System.Windows.MessageBox.Show(
                $"要重新啟用這個 USB 儲存介面嗎？\n\n{record.DisplayName}\n\n" +
                "需要 Windows 管理員確認。只有確定來源可信時才建議重新啟用。",
                "重新啟用裝置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _pnpControlBusy = true;
            UpdateActionDeviceContext();
            try
            {
                int code = await ExecuteElevatedPnPUtilAsync("/enable-device", record.InstanceId);
                if (code != 0)
                {
                    Log($"重新啟用命令未成功（結束碼 {code}）：{record.DisplayName}");
                    System.Windows.MessageBox.Show("Windows 未回報成功；保留停用紀錄，請查看裝置管理員。", "重新啟用未完成");
                    return;
                }
                PnpState after = new PnpState();
                try
                {
                    for (int trial = 0; trial < 3; trial++)
                    {
                        if (trial > 0) await Task.Delay(900);
                        after = await PnpStateProbe.QueryAsync(record.InstanceId);
                        if (after.State == DeviceEnableState.Enabled) break;
                    }
                }
                catch (Exception ex) { Log("重新啟用狀態驗證失敗：" + ex.Message); }
                bool verified = after.State == DeviceEnableState.Enabled;
                if (verified)
                {
                    _disabledStorageDevices.Remove(record.InstanceId);
                    SaveJson(DisabledPath, _disabledStorageDevices);
                    UpdateDisabledDeviceChoices();
                    Log("Windows 已驗證 USB 儲存介面重新啟用：" + record.DisplayName);
                    System.Windows.MessageBox.Show("Windows 已回報裝置正常運作。", "已驗證重新啟用");
                }
                else
                {
                    Log("重新啟用命令成功，仍未驗證裝置正常：" + record.DisplayName);
                    System.Windows.MessageBox.Show("重新啟用命令已送出，但狀態尚未確認。保留原紀錄，請查看裝置管理員或重新插拔。", "仍需確認");
                }
                _ = RefreshDeviceListAsync();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log("使用者取消重新啟用的管理員授權");
            }
            catch (Exception ex)
            {
                Log("重新啟用命令失敗：" + ex.Message);
                System.Windows.MessageBox.Show("無法重新啟用：" + ex.Message);
            }
            finally { _pnpControlBusy = false; UpdateActionDeviceContext(); }
        }

        private static async Task<int> ExecuteElevatedPnPUtilAsync(string verb, string instanceId)
        {
            if (!IsStorageInstanceId(instanceId) ||
                (verb != "/disable-device" && verb != "/enable-device"))
                throw new InvalidOperationException("只允許針對 USBSTOR 儲存介面執行停用／重新啟用。");
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Windows 找不到 PnPUtil 工具。", exe);
            var start = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = verb + " \"" + instanceId + "\"",
                Verb = "runas",
                UseShellExecute = true
            };
            using Process process = Process.Start(start) ??
                throw new IOException("無法啟動 Windows 裝置管理程序。");
            // We do not kill an elevated process on timeout: it might be finishing an OS operation.
            Task wait = process.WaitForExitAsync();
            if (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(60))) != wait)
                throw new TimeoutException("系統裝置操作超過 60 秒；結果未知，請用裝置管理員確認。勿重複嘗試。");
            await wait;
            return process.ExitCode;
        }

        private void UpdateDisabledDeviceChoices()
        {
            if (DisabledStorageCombo == null) return;
            string? original = (DisabledStorageCombo.SelectedItem as DisabledStorageRecord)?.InstanceId;
            DisabledStorageCombo.ItemsSource = _disabledStorageDevices.Values
                .OrderByDescending(x => x.RecordedUtc).ToList();
            DisabledStorageCombo.SelectedItem = _disabledStorageDevices.Values.FirstOrDefault(x => x.InstanceId == original)
                ?? _disabledStorageDevices.Values.OrderByDescending(x => x.RecordedUtc).FirstOrDefault();
        }
    }
}
