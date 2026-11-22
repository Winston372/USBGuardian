using System;
using System.Windows;
using System.Windows.Controls;
using USBGuardian.App.Services;

namespace USBGuardian.App.UI
{
    public partial class UsageNoticeWindow : Window
    {
        private readonly bool _readOnly;
        private bool _reachedBottom;

        public UsageNoticeWindow(string notice, bool readOnly = false)
        {
            InitializeComponent();
            _readOnly = readOnly;
            NoticeBody.Text = notice;
            if (UiPreferences.Language == "en")
            {
                Title = "USB Guardian | Information and limitations";
                HeadingText.Text = "Information and limitations";
                IntroText.Text = "Read the complete notice before deciding whether to continue.";
                QuickSummaryText.Text = "USB Guardian provides alerts after connection; it cannot guarantee pre-connection blocking or malware detection. Back up important files before making changes.";
                ConsentLabel.Text = "I have read and understand the capabilities, limitations, data handling and risks described above. I agree to continue.";
                DeclineButton.Content = "Decline and exit";
                AcceptButton.Content = "Agree and continue";
                ReadProgressText.Text = "Scroll to the bottom of the document to enable the agreement checkbox.";
            }
            if (_readOnly)
            {
                HeadingText.Text = UiPreferences.Runtime("使用須知與風險說明", "Information and limitations");
                IntroText.Text = UiPreferences.Runtime("以下為目前版本的使用說明。", "Current usage information.");
                ConsentCheck.Visibility = Visibility.Collapsed;
                ReadProgressText.Visibility = Visibility.Collapsed;
                DeclineButton.Visibility = Visibility.Collapsed;
                AcceptButton.Content = UiPreferences.Runtime("關閉", "Close");
                AcceptButton.IsEnabled = true;
                Title = UiPreferences.Runtime("USB Guardian｜使用須知", "USB Guardian | Usage information");
            }
        }

        private void NoticeScroll_Loaded(object sender, RoutedEventArgs e) => UpdateReadGate();

        private void NoticeScroll_Changed(object sender, ScrollChangedEventArgs e) => UpdateReadGate();

        private void UpdateReadGate()
        {
            if (_readOnly || _reachedBottom || NoticeScroll == null) return;
            // ScrollableHeight is computed from the rendered extent, not text length.
            // The reading gate stays closed until the entire document is reachable
            // and the viewport reaches the bottom (wheel, keyboard, or scrollbar).
            if (NoticeScroll.ViewportHeight <= 0 || NoticeScroll.ExtentHeight <= 0) return;
            if (NoticeScroll.ScrollableHeight <= 1 ||
                NoticeScroll.VerticalOffset >= NoticeScroll.ScrollableHeight - 2)
            {
                _reachedBottom = true;
                ConsentCheck.IsEnabled = true;
                ReadProgressText.Text = UiPreferences.Runtime(
                    "已到達文件底部。確認理解後，才能選擇同意。",
                    "End of document reached. Confirm your understanding to continue.");
                ReadProgressText.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            }
        }

        private void ConsentChanged(object sender, RoutedEventArgs e)
        {
            if (!_readOnly && ConsentCheck != null && AcceptButton != null)
                AcceptButton.IsEnabled = _reachedBottom && ConsentCheck.IsChecked == true;
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            if (_readOnly || (_reachedBottom && ConsentCheck.IsChecked == true))
                DialogResult = true;
        }

        private void Decline_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
