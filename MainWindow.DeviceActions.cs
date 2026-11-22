using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using USBGuardian.App.Services;

namespace USBGuardian.App
{
    public partial class MainWindow
    {
        private bool _actionDeviceSelectionSync;

        private DeviceItem? SelectedManagedDevice() =>
            MainTabs.SelectedIndex == 3
                ? ActionDeviceCombo.SelectedItem as DeviceItem
                : DeviceList.SelectedItem as DeviceItem;

        private void NavigateDeviceActions_Click(object sender, RoutedEventArgs e)
        {
            DeviceItem? current = DeviceList.SelectedItem as DeviceItem;
            if (current != null) ActionDeviceCombo.SelectedItem = current;
            MainTabs.SelectedIndex = 3;
            UpdateNavAppearance();
        }

        private void NavigateToDevices_Click(object sender, RoutedEventArgs e)
        {
            MainTabs.SelectedIndex = 2;
            UpdateNavAppearance();
        }

        private void RefreshActionDeviceChoices()
        {
            if (ActionDeviceCombo == null) return;
            string? before = (ActionDeviceCombo.SelectedItem as DeviceItem)?.InstanceId ??
                (DeviceList.SelectedItem as DeviceItem)?.InstanceId;
            _actionDeviceSelectionSync = true;
            ActionDeviceCombo.ItemsSource = _devices.Where(d => !d.IsSystemComponent)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            DeviceItem? keep = _devices.FirstOrDefault(d => string.Equals(d.InstanceId, before, StringComparison.OrdinalIgnoreCase) && !d.IsSystemComponent);
            ActionDeviceCombo.SelectedItem = keep;
            _actionDeviceSelectionSync = false;
            UpdateActionDeviceContext();
        }

        private void ActionDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_actionDeviceSelectionSync) return;
            UpdateActionDeviceContext();
        }

        private void UpdateActionDeviceContext()
        {
            if (ActionDeviceContextText == null || NicknameBox == null ||
                ActionStatusHeadline == null || ActionStatusNote == null ||
                MarkAttentionActionButton == null || DisableStorageActionButton == null)
                return;

            if (ActionDeviceCombo.SelectedItem is not DeviceItem item)
            {
                ActionStatusHeadline.Text = UiPreferences.Runtime("請先選擇裝置", "Choose a device");
                ActionDeviceContextText.Text = UiPreferences.Runtime(
                    "選擇後可查看提醒與停用選項。", "Choose a device to see alert and disable options.");
                ActionStatusNote.Text = "";
                MarkAttentionActionButton.IsEnabled = false;
                DisableStorageActionButton.IsEnabled = false;
                NicknameBox.Text = "";
                return;
            }

            _familiar.TryGetValue(Fingerprint(item.InstanceId), out FamiliarRecord? known);
            NicknameBox.Text = known?.Label ?? "";
            // Stored records document what the app last verified, not a live PnP guarantee.
            bool hasDisableRecord = _disabledStorageDevices.TryGetValue(item.InstanceId, out var disabled);
            bool isStorage = IsStorageInstanceId(item.InstanceId) &&
                string.Equals(item.Class, "DiskDrive", StringComparison.OrdinalIgnoreCase);

            if (hasDisableRecord && disabled != null)
            {
                ActionStatusHeadline.Text = disabled.VerifiedDisabled
                    ? UiPreferences.Runtime("上次已確認停用", "Previously verified disabled")
                    : UiPreferences.Runtime("停用狀態尚未確認", "Disable status not verified");
                ActionDeviceContextText.Text = UiPreferences.Runtime(
                    "這是上次操作的紀錄；如需確認目前狀態，請在 Windows 裝置管理員檢查。",
                    "This is a record of the last action. Check Device Manager for the current state.");
            }
            else if (item.IsUntrusted)
            {
                ActionStatusHeadline.Text = UiPreferences.Runtime(
                    "已列為需注意（尚未停用）", "Marked for attention (still enabled)");
                ActionDeviceContextText.Text = UiPreferences.Runtime(
                    "下次偵測到相同裝置項目時會提醒。裝置目前仍可能正常使用。",
                    "You will be alerted if this device entry is detected again. It may still be usable.");
            }
            else
            {
                ActionStatusHeadline.Text = UiPreferences.Runtime("尚未標記", "Not marked");
                ActionDeviceContextText.Text = UiPreferences.Runtime(
                    "沒有設定提醒或停用紀錄。", "No alert marker or disable record.");
            }

            ActionStatusNote.Text = item.Name + " · " + item.LocalizedCategory;
            MarkAttentionActionButton.IsEnabled = !item.IsUntrusted;
            DisableStorageActionButton.IsEnabled =
                item.IsUntrusted && isStorage && !hasDisableRecord && !_pnpControlBusy;
            DisableStorageActionButton.ToolTip = !isStorage
                ? UiPreferences.Runtime("僅支援 USBSTOR 儲存介面；不會停用鍵盤或網路卡。",
                    "Only USBSTOR storage interfaces are supported, not keyboards or network adapters.")
                : !item.IsUntrusted
                ? UiPreferences.Runtime("請先按『列為需注意』。", "First mark this device for attention.")
                : hasDisableRecord
                ? UiPreferences.Runtime("已有停用紀錄。可在『其他操作與復原』處理。",
                    "A disable record exists. See Other actions and recovery.")
                : UiPreferences.Runtime("將要求 Windows 管理員確認停用。", "Windows administrator approval is required.");
        }
    }
}
