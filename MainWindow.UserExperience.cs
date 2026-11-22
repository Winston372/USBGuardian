using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using USBGuardian.App.Services;

namespace USBGuardian.App
{
    public partial class MainWindow
    {
        private bool _preferencesUpdating;

        private void InitializePreferenceControls()
        {
            _preferencesUpdating = true;
            ChooseComboTag(ThemeChoice, UiPreferences.Theme);
            ChooseComboTag(LanguageChoice, UiPreferences.Language);
            _preferencesUpdating = false;
            MainTabs.SelectedIndex = 0;
            UpdateHomeSummary();
            UpdateNavAppearance();
            // React to Windows theme changes without polling.
            SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        }
        private void StopPreferenceControls()
        {
            SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        }
        private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UiPreferences.RefreshSystemTheme();
                UpdateNavAppearance();
            }));
        }
        private static void ChooseComboTag(ComboBox combo, string value)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal))
                { combo.SelectedItem = item; return; }
            }
        }
        private void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_preferencesUpdating || ThemeChoice?.SelectedItem is not ComboBoxItem selected) return;
            UiPreferences.SetTheme(selected.Tag?.ToString() ?? "system");
            UpdateNavAppearance();
        }
        private void LanguageChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_preferencesUpdating || LanguageChoice?.SelectedItem is not ComboBoxItem selected) return;
            UiPreferences.SetLanguage(selected.Tag?.ToString() ?? "zh-TW");
            UpdateHomeSummary();
            if (_tray?.ContextMenuStrip is System.Windows.Forms.ContextMenuStrip menu &&
                menu.Items.Count >= 4)
            {
                menu.Items[0].Text = UiPreferences.Runtime("開啟 USB Guardian", "Open USB Guardian");
                menu.Items[1].Text = UiPreferences.Runtime("測試通知", "Test notification");
                menu.Items[3].Text = UiPreferences.Runtime("完全結束", "Exit USB Guardian");
            }
            // Filters use the immutable Chinese tags, never their translated labels.
            RefreshDeviceFilters();
            RefreshHomeActivityRows();
            UpdateMonitoringIndicator();
            UpdateNavAppearance();
        }
        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Bubbling selection changes from nested lists must not trigger navigation updates.
            if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
            UpdateNavAppearance();
        }
        private void HomeInspect_Click(object sender, RoutedEventArgs e)
        {
            MainTabs.SelectedIndex = 1;
            UpdateNavAppearance();
        }
        private void HomeDevices_Click(object sender, RoutedEventArgs e)
        {
            MainTabs.SelectedIndex = 2;
            UpdateNavAppearance();
        }
        private void UpdateNavAppearance()
        {
            if (SidebarNavPanel == null || MainTabs == null) return;
            string[] pageTitles = UiPreferences.Language == "en"
                ? new[] { "Home", "Check USB", "My devices", "Device controls", "Activity", "Settings" }
                : new[] { "首頁", "檢查 USB", "我的裝置", "裝置操作", "活動紀錄", "設定" };
            string[] pageHints = UiPreferences.Language == "en"
                ? new[] { "Device activity", "External USB drives", "Connection and device details", "Trust and device controls", "Alerts and quarantine", "Display and startup" }
                : new[] { "USB 連接紀錄", "外接 USB 磁碟", "裝置來源與狀態", "信任與系統停用", "提醒與隔離檔案", "顯示與啟動選項" };
            int selected = MainTabs.SelectedIndex;
            if (selected >= 0 && selected < pageTitles.Length && CurrentPageHeading != null)
            {
                CurrentPageHeading.Text = pageTitles[selected];
                CurrentPageSubtitle.Text = pageHints[selected];
            }
            foreach (Button button in SidebarNavPanel.Children.OfType<Button>())
            {
                if (int.TryParse(button.Tag?.ToString(), out int index) &&
                    index == MainTabs.SelectedIndex)
                {
                    button.Background = (Brush)System.Windows.Application.Current.Resources["NavSelectedBrush"];
                    button.BorderBrush = (Brush)System.Windows.Application.Current.Resources["AccentBrush"];
                    button.BorderThickness = new Thickness(3, 0, 0, 0);
                    button.Foreground = (Brush)System.Windows.Application.Current.Resources["AccentTextBrush"];
                    button.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    button.Background = System.Windows.Media.Brushes.Transparent;
                    button.BorderBrush = System.Windows.Media.Brushes.Transparent;
                    button.BorderThickness = new Thickness(3, 0, 0, 0);
                    button.Foreground = (Brush)System.Windows.Application.Current.Resources["TextBrush"];
                    button.FontWeight = FontWeights.Normal;
                }
            }
        }
        private void UpdateHomeSummary()
        {
            if (HomeHeadlineText == null || HomeAdviceText == null) return;
            int critical = _devices.Count(x => !x.IsSystemComponent &&
                (x.AttentionLevel >= 2 || x.HadFunctionChange || x.IsUntrusted) &&
                (x.IsNewObservation || x.IsUntrusted));
            if (critical > 0)
            {
                HomeHeadlineText.Text = UiPreferences.Runtime(
                    $"有 {critical} 筆裝置紀錄待確認",
                    $"{critical} device entries to review");
                HomeAdviceText.Visibility = Visibility.Visible;
                HomeAdviceText.Text = UiPreferences.Runtime(
                    "請確認是否與剛連接的裝置有關。",
                    "Check whether these entries match a device you connected.");
            }
            else if (_savedAlerts.Any(x => !x.Read))
            {
                HomeHeadlineText.Text = UiPreferences.Runtime(
                    $"有 {_savedAlerts.Count(x => !x.Read)} 則未讀提醒",
                    $"{_savedAlerts.Count(x => !x.Read)} alerts to review");
                HomeAdviceText.Visibility = Visibility.Visible;
                HomeAdviceText.Text = UiPreferences.Runtime(
                    "請到活動紀錄查看。",
                    "See Activity for details.");
            }
            else
            {
                HomeHeadlineText.Text = UiPreferences.Runtime(
                    "目前沒有未讀提醒",
                    "No unread alerts");
                HomeAdviceText.Visibility = Visibility.Collapsed;
            }
            UpdateMonitoringIndicator();
        }
    }
}
