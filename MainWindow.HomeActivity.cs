using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using USBGuardian.App.Services;
using USBGuardian.App.UI;

namespace USBGuardian.App
{
    // A presentation model for the Home list. It does not modify or reinterpret
    // the underlying event log, which remains accessible in Activity.
    public sealed class HomeActivityRow
    {
        public string Raw { get; init; } = "";
        public string Title { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Detail { get; init; } = "";
        public string TimeLabel { get; init; } = "";
        public string? Drive { get; init; }
    }

    public partial class MainWindow
    {
        private static HomeActivityRow BuildActivityRow(string logLine)
        {
            Match line = Regex.Match(logLine, @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*(?<text>.*)$");
            string detail = line.Success ? line.Groups["text"].Value.Trim() : logLine.Trim();
            string time = line.Success ? line.Groups["time"].Value[..5] : "";
            string title = detail;
            string summary = "";
            // The drive label is only used if the current safe USB picker actually contains it.
            Match letter = Regex.Match(detail, @"(?<![A-Za-z0-9])(?<disk>[A-Za-z]):\\");
            string? drive = letter.Success ? letter.Groups["disk"].Value.ToUpperInvariant() + @":\" : null;
            bool english = UiPreferences.Language == "en";

            if (detail.Contains("偵測到磁碟插入", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "USB drive connected" : "有新磁碟接入";
                summary = drive ?? (english ? "New storage device" : "新的儲存裝置");
            }
            else if (detail.Contains("磁碟已移除", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "USB drive removed" : "磁碟已移除";
                summary = drive ?? "";
            }
            else if (detail.Contains("PnP 新增", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("偵測到新裝置", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "Device change detected" : "偵測到裝置變動";
                summary = english ? "Windows device records changed" : "Windows 裝置紀錄更新";
            }
            else if (detail.Contains("SafeLab 完成", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("預覽已完成", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "File preview finished" : "檔案預覽完成";
                int colon = detail.IndexOf('：');
                summary = colon >= 0 ? detail[(colon + 1)..].Trim() : "SafeLab";
            }
            else if (detail.Contains("已檢視資料夾", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "Folder viewed" : "已查看資料夾";
                summary = drive ?? "";
            }
            else if (detail.Contains("不信任", StringComparison.OrdinalIgnoreCase) ||
                     detail.Contains("需注意", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "Device attention setting changed" : "已更新裝置提醒";
                summary = english ? "Check the selected device" : "可到裝置操作查看";
            }
            else if (detail.Contains("Defender", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "Defender scan update" : "Defender 掃描狀態";
                summary = drive ?? "Microsoft Defender";
            }
            else if (detail.Contains("已啟動", StringComparison.OrdinalIgnoreCase))
            {
                title = english ? "USB monitoring started" : "USB 監控已啟動";
                summary = english ? "Waiting for device events" : "等待裝置接入";
            }
            else
            {
                // Preserve the author's original event meaning; don't invent a verdict.
                int split = detail.IndexOf('：');
                if (split > 0 && split < 32)
                {
                    title = detail[..split];
                    summary = detail[(split + 1)..].Trim();
                }
            }
            return new HomeActivityRow
            {
                Raw = logLine, Title = title, Summary = summary,
                Detail = detail, Drive = drive, TimeLabel = time
            };
        }

        private void RefreshHomeActivityRows()
        {
            if (RecentList == null) return;
            RecentList.Items.Clear();
            foreach (string log in _logEvents.Take(12))
                RecentList.Items.Add(BuildActivityRow(log));
            if (HomeEventPreview != null) HomeEventPreview.Visibility = Visibility.Collapsed;
        }

        private void RecentInspect_Click(object sender, RoutedEventArgs e)
        {
            HomeActivityRow? row = (sender as Button)?.Tag as HomeActivityRow;
            MainTabs.SelectedIndex = 1;
            UpdateNavAppearance();
            // Never navigate to an internal disk or to stale media based on log text.
            if (row?.Drive != null && DriveCombo.Items.Contains(row.Drive))
                DriveCombo.SelectedItem = row.Drive;
            e.Handled = true;
        }

        private void RecentDeviceActions_Click(object sender, RoutedEventArgs e)
        {
            // Navigation only; no device is silently selected, trusted or disabled.
            MainTabs.SelectedIndex = 3;
            UpdateNavAppearance();
            e.Handled = true;
        }

        private void RecentViewFull_Click(object sender, RoutedEventArgs e)
        {
            if (RecentList.SelectedItem is not HomeActivityRow row) return;
            MainTabs.SelectedIndex = 4;
            TechnicalLogExpander.IsExpanded = true;
            UpdateNavAppearance();
            foreach (object item in LogList.Items)
            {
                if (item is string log && string.Equals(log, row.Raw, StringComparison.Ordinal))
                {
                    LogList.SelectedItem = item;
                    LogList.ScrollIntoView(item);
                    break;
                }
            }
            e.Handled = true;
        }

        private void UpdateMonitoringIndicator()
        {
            if (MonitoringStatusDot == null || SidebarStatus == null) return;
            int unread = _savedAlerts.Count(x => !x.Read);
            int attention = _savedAlerts.Count(x => !x.Read && x.Attention);
            bool checking = _safeLabBusy || _defenderBusy;
            bool urgentToast = _toast != null && _toast.IsVisible &&
                               _toast.Severity == NotificationSeverity.Critical;

            string label;
            Color tone;
            if (!_initialized)
            {
                label = UiPreferences.Runtime("尚未啟動", "Not started");
                tone = Color.FromRgb(143, 152, 164);
            }
            else if (urgentToast)
            {
                label = UiPreferences.Runtime("有待處理的提醒", "Action needed");
                tone = Color.FromRgb(212, 80, 86);
            }
            else if (checking)
            {
                label = UiPreferences.Runtime("正在檢查", "Checking");
                tone = Color.FromRgb(59, 132, 209);
            }
            else if (attention > 0)
            {
                label = UiPreferences.Runtime("有新的提醒", "New alerts");
                tone = Color.FromRgb(210, 146, 45);
            }
            else
            {
                label = UiPreferences.Runtime("監控中", "Monitoring");
                tone = Color.FromRgb(38, 160, 117);
            }
            MonitoringStatusDot.Fill = new SolidColorBrush(tone);
            SidebarStatus.Text = label;
            if (MonitorTooltipTitle != null) MonitorTooltipTitle.Text = label;
            if (MonitorTooltipDetails != null)
                MonitorTooltipDetails.Text = UiPreferences.Runtime(
                    $"USB 接入提醒：{(_initialized ? "已啟用" : "未啟用")}\n未讀提醒：{unread} 則\n不代表所有裝置已通過安全檢查。",
                    $"USB alerts: {(_initialized ? "On" : "Off")}\nUnread alerts: {unread}\nMonitoring is not a malware verdict.");
        }
    }
}
