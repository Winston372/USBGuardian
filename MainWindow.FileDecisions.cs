using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using USBGuardian.App.Services;

namespace USBGuardian.App
{
    public partial class MainWindow
    {
        // Kept the old XAML handler name to preserve compatibility with existing UI bindings.
        // V3.0 DOES NOT delete files on preview: the action is reversible quarantine.
        private async void DeletePreviewedFile_Click(object sender, RoutedEventArgs e)
        {
            if (_safeLabBusy || _selectedFile is not FileEntry selected ||
                _currentDrive == null || _reviewedFilePath == null || _reviewedFileHash == null ||
                !string.Equals(_reviewedFilePath, selected.Path, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(UiPreferences.Runtime(
                    "請先選取檔案並完成 SafeLab 預覽。", "Select a file and complete SafeLab preview first."));
                return;
            }
            string fullPath = selected.Path;
            string sha = _reviewedFileHash;
            string drive = _currentDrive;
            var answer = MessageBox.Show(UiPreferences.Runtime(
                $"要隔離以下檔案嗎？\n\n{selected.Name}\n\n檔案將移出 USB，改以加密形式保存在本機隔離區，可稍後還原。原始資料將在隔離成功後才刪除。請先關閉任何正在使用此檔案的程式。",
                $"Quarantine this file?\n\n{selected.Name}\n\nThe file will be removed from the USB after an encrypted recoverable copy is saved on this computer. Close programs using it first."),
                UiPreferences.Runtime("隔離所選檔案", "Quarantine selected file"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            DeletePreviewedFileButton.IsEnabled = false;
            try
            {
                QuarantineRecord record = await Task.Run(() =>
                    QuarantineStore.Quarantine(fullPath, sha, drive));
                _reviewedFilePath = null;
                _reviewedFileHash = null;
                _selectedFile = null;
                SafeLabStatusText.Text = record.RemovalConfirmed
                    ? UiPreferences.Runtime("檔案已移至可復原隔離區", "File moved to recoverable quarantine")
                    : UiPreferences.Runtime("檔案已加密保存，但紀錄需要核對；請查看隔離清單。",
                        "Encrypted copy saved, but the record needs checking in History.");
                SafeLabPreviewImage.Source = null;
                SafeLabPreviewImage.Visibility = Visibility.Collapsed;
                SafeLabTextPanel.Visibility = Visibility.Collapsed;
                SafeLabPreviewHint.Visibility = Visibility.Visible;
                SafeLabPreviewHint.Text = UiPreferences.Runtime(
                    "如需還原，請前往「歷史紀錄」→「隔離檔案」。",
                    "To restore, open History → Quarantined files.");
                if (FileTree.SelectedItem is TreeViewItem node)
                {
                    ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(node);
                    parent?.Items.Remove(node);
                }
                RefreshQuarantineList();
                FileSummaryText.Text = record.RemovalConfirmed
                    ? UiPreferences.Runtime($"已隔離 {record.FileName}；可以在歷史紀錄中還原。",
                        $"Quarantined {record.FileName}. Restore it from History.")
                    : UiPreferences.Runtime("隔離紀錄需核對，請不要直接執行原檔；請查看歷史紀錄。",
                        "Quarantine status needs verification. Do not open the original file.");
                Log("使用者隔離檔案：" + record.FileName);
            }
            catch (Exception ex)
            {
                DeletePreviewedFileButton.IsEnabled = true;
                MessageBox.Show(UiPreferences.Runtime("未完成隔離：", "Quarantine failed: ") + ex.Message,
                    "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshQuarantine_Click(object sender, RoutedEventArgs e) => RefreshQuarantineList();

        private void RefreshQuarantineList()
        {
            if (QuarantineList == null) return;
            try
            {
                QuarantineList.ItemsSource = QuarantineStore.List();
                QuarantineSummaryText.Text = UiPreferences.Runtime(
                    $"目前有 {QuarantineStore.List().Count} 個已隔離檔案。只有你主動選擇才會還原或永久刪除。",
                    $"{QuarantineStore.List().Count} quarantined files. Restore or delete only when you choose.");
            }
            catch (Exception ex)
            {
                QuarantineSummaryText.Text = UiPreferences.Runtime("無法讀取隔離清單：", "Cannot read quarantine list: ") + ex.Message;
            }
        }

        private async void RestoreQuarantine_Click(object sender, RoutedEventArgs e)
        {
            if (QuarantineList.SelectedItem is not QuarantineRecord record)
            {
                MessageBox.Show(UiPreferences.Runtime("請先選擇一個隔離檔案。", "Choose a quarantined file first."));
                return;
            }
            if (MessageBox.Show(UiPreferences.Runtime(
                    $"將 {record.FileName} 還原到原本的 USB 位置？\n\n{record.OriginalPath}\n\n還原前請確認檔案來源可信，不會自動開啟它。",
                    $"Restore {record.FileName} to its original USB path?\n\n{record.OriginalPath}\n\nOnly restore if you trust the source. The file will not be opened."),
                    UiPreferences.Runtime("確認還原", "Confirm restore"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await Task.Run(() => QuarantineStore.Restore(record.Id));
                Log("使用者還原隔離檔案：" + record.FileName);
                RefreshQuarantineList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiPreferences.Runtime("無法還原：", "Restore failed: ") + ex.Message,
                    "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void RemoveQuarantine_Click(object sender, RoutedEventArgs e)
        {
            if (QuarantineList.SelectedItem is not QuarantineRecord record)
            {
                MessageBox.Show(UiPreferences.Runtime("請先選擇一個隔離檔案。", "Choose a quarantined file first."));
                return;
            }
            if (MessageBox.Show(UiPreferences.Runtime(
                    $"永久清除隔離區中的 {record.FileName}？\n\n此操作無法從 USB Guardian 還原。",
                    $"Permanently remove {record.FileName} from quarantine?\n\nUSB Guardian cannot undo this action."),
                    UiPreferences.Runtime("永久刪除隔離檔案", "Permanently remove from quarantine"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await Task.Run(() => QuarantineStore.DeletePermanently(record.Id));
                Log("使用者永久清除隔離副本：" + record.FileName);
                RefreshQuarantineList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiPreferences.Runtime("無法移除：", "Removal failed: ") + ex.Message,
                    "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void ManageDeviceFromPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDrive == null)
            {
                MessageBox.Show(UiPreferences.Runtime("請先選擇 USB 磁碟。", "Select a USB drive first."));
                return;
            }
            string drive = _currentDrive;
            DeviceStatusText.Text = UiPreferences.Runtime(
                "正在確認磁碟與 Windows 裝置的關聯...", "Matching drive to a Windows device...");
            try
            {
                // Match an exact USBSTOR DiskDrive PnP instance. Never guess by VID/PID or name.
                string? deviceId = await UsbDriveMapping.ResolveStorageInstanceAsync(drive, _closingToken.Token);
                await RefreshDeviceListAsync();
                DeviceItem? device = deviceId == null ? null : _devices.FirstOrDefault(x =>
                    x.Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) &&
                    x.InstanceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
                MainTabs.SelectedIndex = 2;
                UpdateNavAppearance();
                if (device == null)
                {
                    DeviceStatusText.Text = UiPreferences.Runtime(
                        "無法可靠確認這個磁碟對應的實體裝置，因此不提供一鍵停用。你仍可查看清單，但請勿依名稱猜測。",
                        "Cannot reliably match this drive to a physical device. One-click disable is unavailable; do not guess by device name.");
                    return;
                }
                DeviceOriginFilter.SelectedIndex = 0;
                DeviceCategoryFilter.SelectedIndex = 0;
                DeviceAttentionFilter.SelectedIndex = 0;
                DeviceHideSystemCheck.IsChecked = false;
                DeviceSearchBox.Text = "";
                RefreshDeviceFilters();
                DeviceList.SelectedItem = device;
                DeviceList.ScrollIntoView(device);
                DeviceStatusText.Text = UiPreferences.Runtime(
                    $"已找到磁碟 {drive} 對應的 Windows 儲存裝置。操作前請確認名稱。",
                    $"Matched {drive} to a Windows storage device. Confirm its name before any action.");
            }
            catch (Exception ex)
            {
                DeviceStatusText.Text = UiPreferences.Runtime(
                    "裝置對應未完成；不會自動停用任何裝置。原因：",
                    "Device matching failed; no device was disabled. Reason: ") + ex.Message;
            }
        }
    }
}
