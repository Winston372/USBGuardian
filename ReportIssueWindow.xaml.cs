using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using USBGuardian.App.Services;

namespace USBGuardian.App.UI
{
    /// <summary>
    /// User-controlled feedback composer. It does not send email or upload diagnostics
    /// automatically. Users review the message in their mail client before sending.
    /// </summary>
    public partial class ReportIssueWindow : Window
    {
        private const string SupportAddress = "winston00000423@gmail.com";
        private readonly Exception? _exception;
        private readonly string _context;
        private readonly bool _en;

        public ReportIssueWindow(Exception? exception = null, string? context = null)
        {
            _exception = exception;
            _context = context ?? "";
            _en = UiPreferences.Language == "en";
            InitializeComponent();
            SetupLabels();
            if (_exception != null)
            {
                Heading.Text = T("程式發生錯誤", "An error occurred");
                Introduction.Text = T("可以把這次錯誤回報給開發者。請先檢查內容，再自行寄出郵件。",
                    "You can report this error. Review the message before sending from your email app.");
                CategoryChoice.SelectedIndex = 0;
                SummaryInput.Text = T("程式發生錯誤", "Application error");
                DescriptionInput.Text = T("操作時程式發生問題。", "The app encountered an error during use.");
            }
            else CategoryChoice.SelectedIndex = 1;
            Loaded += (_, _) => SummaryInput.Focus();
        }

        private string T(string zh, string en) => _en ? en : zh;

        private void SetupLabels()
        {
            Title = T("回報問題 · USB Guardian", "Report a problem · USB Guardian");
            Heading.Text = T("回報問題", "Report a problem");
            Introduction.Text = T("簡單描述問題即可。建議填寫發生經過，方便重現。",
                "Describe what happened so we can reproduce the issue.");
            TypeLabel.Text = T("問題類型", "Issue type");
            SummaryLabel.Text = T("問題簡述 *", "Short summary *");
            WhatLabel.Text = T("發生了什麼事？ *", "What happened? *");
            StepsLabel.Text = T("如何再次遇到這個問題？（選填）", "Steps to reproduce (optional)");
            EmailLabel.Text = T("你的聯絡信箱（選填）", "Contact email (optional)");
            IncludeDiagnostics.Content = T("附上程式版本與錯誤摘要（選填）", "Include app version and error summary (optional)");
            PrivacyNote.Text = T("送出前請檢查內容，勿提供密碼、金鑰或私人檔案。截圖可在郵件中自行附加。",
                "Review before sending. Never include passwords, keys or private files. Add screenshots in your mail app if needed.");
            CancelButton.Content = T("取消", "Cancel");
            CopyButton.Content = T("複製內容", "Copy report");
            ComposeButton.Content = T("開啟郵件草稿", "Open email draft");
            CategoryChoice.Items.Clear();
            foreach (string category in new[] {
                T("程式錯誤或閃退", "Crash or error"),
                T("畫面與操作", "Interface or usability"),
                T("USB 偵測與掃描", "USB detection or scanning"),
                T("檔案隔離與還原", "File quarantine or restore"),
                T("裝置操作", "Device controls"),
                T("功能建議或其他", "Suggestion or other")})
                CategoryChoice.Items.Add(new ComboBoxItem { Content = category });
        }

        private bool IsValid()
        {
            if (string.IsNullOrWhiteSpace(SummaryInput.Text) ||
                string.IsNullOrWhiteSpace(DescriptionInput.Text))
            {
                MessageBox.Show(this, T("請先填寫問題簡述和發生情況。", "Enter a summary and describe what happened."),
                    T("還缺少資料", "Missing information"), MessageBoxButton.OK, MessageBoxImage.Information);
                if (string.IsNullOrWhiteSpace(SummaryInput.Text)) SummaryInput.Focus();
                else DescriptionInput.Focus();
                return false;
            }
            // Optional email must be syntactically plausible if provided.
            string email = EmailInput.Text.Trim();
            if (email.Length > 0 && (email.Contains(' ') || email.IndexOf('@') < 1 ||
                                     email.LastIndexOf('.') < email.IndexOf('@') + 2))
            {
                MessageBox.Show(this, T("聯絡信箱格式似乎不正確，請確認後再試。",
                    "Please check the contact email address."), T("請確認信箱", "Check email"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                EmailInput.Focus();
                return false;
            }
            return true;
        }

        private string ComposeBody()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("USB Guardian · " + T("問題回報", "Issue report"));
            sb.AppendLine("---");
            sb.AppendLine(T("類型", "Type") + ": " + (CategoryChoice.SelectedItem as ComboBoxItem)?.Content);
            sb.AppendLine(T("簡述", "Summary") + ": " + SummaryInput.Text.Trim());
            sb.AppendLine();
            sb.AppendLine(T("發生情況", "What happened") + ":");
            sb.AppendLine(DescriptionInput.Text.Trim());
            sb.AppendLine();
            sb.AppendLine(T("重現步驟", "Steps to reproduce") + ":");
            sb.AppendLine(StepsInput.Text.Trim());
            if (!string.IsNullOrWhiteSpace(EmailInput.Text))
                sb.AppendLine(T("聯絡信箱", "Contact") + ": " + EmailInput.Text.Trim());
            if (IncludeDiagnostics.IsChecked == true)
            {
                sb.AppendLine();
                sb.AppendLine("--- Diagnostics (user opted in) ---");
                string version = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
                sb.AppendLine("USB Guardian: " + version);
                sb.AppendLine("Windows: " + Environment.OSVersion.VersionString);
                if (_exception != null)
                {
                    sb.AppendLine("Error type: " + _exception.GetType().FullName);
                    // Error message may contain file paths, device names or private data.
                    // Show in editable mail draft for user review; never include full stack trace.
                    string msg = _exception.Message;
                    sb.AppendLine("Error message: " + (msg.Length > 550 ? msg.Substring(0, 550) : msg));
                }
                if (_context.Length > 0) sb.AppendLine("Context: " + _context);
            }
            return sb.ToString();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!IsValid()) return;
            try
            {
                Clipboard.SetText(ComposeBody());
                MessageBox.Show(this, T("已複製回報內容。可貼到郵件中寄給開發者。",
                    "Report copied. Paste it into an email to the developer."),
                    "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Compose_Click(object sender, RoutedEventArgs e)
        {
            if (!IsValid()) return;
            string subject = "USB Guardian - " + SummaryInput.Text.Trim();
            string body = ComposeBody();
            // mailto: has variable length limits across clients. Keep the draft small enough
            // to work reliably; provide full report via clipboard when it is longer.
            if (body.Length > 1800)
            {
                try { Clipboard.SetText(body); } catch { }
                body = body.Substring(0, 1600) + "\n\n" +
                       T("（內容較長，已將完整版本複製到剪貼簿，可貼回郵件編輯器。）",
                         "(Long report: full text copied to clipboard; paste it into your draft.)");
            }
            string uri = "mailto:" + SupportAddress + "?subject=" + Uri.EscapeDataString(subject) +
                         "&body=" + Uri.EscapeDataString(body);
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                MessageBox.Show(this, T("已嘗試開啟郵件草稿。請在郵件程式確認收件者與內容，並親自按「寄送」。",
                    "Email draft requested. Review recipient and content, then press Send in your mail app."),
                    "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                try { Clipboard.SetText(ComposeBody()); } catch { }
                MessageBox.Show(this, T("找不到可開啟的郵件程式。回報內容已嘗試複製到剪貼簿，請自行寄到：",
                    "No email app was available. The report was copied if possible. Send it to:") +
                    "\n" + SupportAddress, "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
