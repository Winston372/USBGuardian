using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using USBGuardian.App.Services;

namespace USBGuardian.App
{
    public partial class MainWindow
    {
        private void AlertList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlertSelectedTitle == null || AlertSelectedInfo == null || AlertSelectedBody == null) return;
            if (AlertList.SelectedItem is not SavedAlert alert)
            {
                AlertSelectedTitle.Text = UiPreferences.Runtime("點選左側提醒", "Select an alert");
                AlertSelectedInfo.Text = "";
                AlertSelectedBody.Text = "";
                return;
            }
            AlertSelectedTitle.Text = alert.Title;
            AlertSelectedInfo.Text = alert.TimeUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") + "  ·  " +
                (alert.Read ? UiPreferences.Runtime("已讀", "Read") : UiPreferences.Runtime("未讀", "Unread"));
            AlertSelectedBody.Text = alert.Details + (string.IsNullOrWhiteSpace(alert.Drive)
                ? "" : "\n" + UiPreferences.Runtime("磁碟：", "Drive: ") + alert.Drive);
        }

        private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HomeEventPreview == null || HomeEventSummaryText == null) return;
            if (RecentList.SelectedItem is not HomeActivityRow selected)
            {
                HomeEventPreview.Visibility = Visibility.Collapsed;
                return;
            }
            // Selecting an event stays on Home; full technical details are opt-in.
            HomeEventSummaryText.Text = selected.Detail;
            HomeEventPreview.Visibility = Visibility.Visible;
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogDetailTitle == null || LogDetailInfo == null || LogDetailBody == null) return;
            if (LogList.SelectedItem is not string text)
            {
                LogDetailTitle.Text = UiPreferences.Runtime("點選紀錄查看詳情", "Select an event to see details");
                LogDetailInfo.Text = "";
                LogDetailBody.Text = "";
                return;
            }
            int end = text.IndexOf(']');
            LogDetailInfo.Text = text.StartsWith("[") && end > 1 ? text.Substring(1, end - 1) : "";
            string detail = end >= 0 ? text[(end + 1)..].TrimStart() : text;
            LogDetailTitle.Text = detail.Length > 110 ? detail[..110] + "…" : detail;
            LogDetailBody.Text = detail.StartsWith("SafeLab 完成", StringComparison.OrdinalIgnoreCase)
                ? UiPreferences.Runtime("已完成受限預覽。這不是病毒掃描結果，也不代表檔案一定安全。",
                                        "Restricted preview completed. This is not a malware scan or a guarantee of safety.")
                : detail.StartsWith("已檢視資料夾", StringComparison.OrdinalIgnoreCase)
                ? UiPreferences.Runtime("已讀取資料夾清單，沒有執行其中的檔案。",
                                        "The folder was listed without executing its files.")
                : UiPreferences.Runtime("這是本次執行的操作紀錄。可依照左側時間核對事件順序。",
                                        "This is an event from the current app session. Use the time to check its sequence.");
        }
    }
}
