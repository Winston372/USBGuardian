using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using USBGuardian.App.Services;

namespace USBGuardian.App.UI
{
    // Presentation severity does NOT represent a virus verdict.
    // Critical is reserved for a confirmed protection condition (such as a user-blocked device),
    // never inferred from filename patterns alone.
    public enum NotificationSeverity { Information, Caution, Critical }

    public partial class NotificationWindow : Window
    {
        private readonly Action _open;
        private readonly Action? _scan;
        public NotificationSeverity Severity { get; }

        public NotificationWindow(string title, string body, NotificationSeverity severity,
                                  Action open, Action? scan)
        {
            InitializeComponent();
            Severity = severity;
            _open = open;
            _scan = scan;

            SubtitleText.Text = title;
            BodyText.Text = body; // Full text is scrollable; no silent truncation.
            ScanButton.Content = UiPreferences.Runtime("Defender 掃描", "Scan with Defender");
            OpenButton.Content = UiPreferences.Runtime("查看詳情", "View details");
            AutomationProperties.SetName(DismissButton,
                UiPreferences.Runtime("關閉通知", "Dismiss notification"));
            ScanButton.Visibility = scan is null ? Visibility.Collapsed : Visibility.Visible;

            string tone, tint;
            switch (severity)
            {
                case NotificationSeverity.Critical:
                    Width = 462;
                    Height = 258;
                    LevelText.Text = UiPreferences.Runtime("需要處理", "Action needed");
                    LevelIcon.Text = "!";
                    tone = "NotificationCriticalBrush";
                    tint = "NotificationCriticalTintBrush";
                    break;
                case NotificationSeverity.Caution:
                    Width = 440;
                    Height = 230;
                    LevelText.Text = UiPreferences.Runtime("需要確認", "Review needed");
                    LevelIcon.Text = "!";
                    tone = "NotificationCautionBrush";
                    tint = "NotificationCautionTintBrush";
                    break;
                default:
                    Width = 416;
                    Height = 204;
                    LevelText.Text = UiPreferences.Runtime("一般資訊", "Information");
                    LevelIcon.Text = "i";
                    tone = "NotificationInfoBrush";
                    tint = "NotificationInfoTintBrush";
                    break;
            }
            ToastSignal.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, tone);
            ToastSignal.Visibility = severity == NotificationSeverity.Information
                ? Visibility.Collapsed : Visibility.Visible;
            LevelIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, tone);
            LevelIconBackground.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, tint);
            if (severity == NotificationSeverity.Critical)
                ToastFrame.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, tone);

            Loaded += (_, _) =>
            {
                // WorkArea already accounts for the taskbar. Keep the popup on-screen,
                // including on small displays; WPF sizes use device-independent pixels.
                Rect work = SystemParameters.WorkArea;
                Width = Math.Min(Width, Math.Max(300, work.Width - 28));
                Height = Math.Min(Height, Math.Max(164, work.Height - 28));
                // Keep the content scroller at the start when first shown.
                ToastBodyScroll.ScrollToTop();
                Left = work.Right - Width - 14;
                Top = work.Bottom - Height - 14;
                BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            };
        }

        private void Open_Click(object sender, RoutedEventArgs e) { Close(); _open(); }
        private void Scan_Click(object sender, RoutedEventArgs e) { Close(); _scan?.Invoke(); }
        private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
    }
}
