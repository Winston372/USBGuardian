using System;
using System.IO;
using System.Threading;
using System.Windows;
using USBGuardian.App.Services;
using USBGuardian.App.UI;

namespace USBGuardian.App
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;
        private bool _ownsMutex;

        private static string StartupLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USBGuardian", "startup-diagnostics.log");

        private static void WriteStartupLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
                File.AppendAllText(StartupLogPath,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must not cause a second startup failure.
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool background = Array.Exists(e.Args, x =>
                string.Equals(x, "--background", StringComparison.OrdinalIgnoreCase));
            try
            {
                bool created;
                _singleInstance = new Mutex(true, @"Local\USBGuardian.Desktop.V12", out created);
                _ownsMutex = created;
                if (!created)
                {
                    WriteStartupLog("Another USB Guardian instance is already running; launch skipped.");
                    if (!background)
                    {
                        MessageBox.Show(
                            "USB Guardian 已在背景執行，因此這次不會再開啟第二份。\n\n" +
                            "請到工作列右下角的通知區（^）找到 USB Guardian 圖標，" +
                            "按兩下可開啟現有視窗。\n\n" +
                            "若要測試新版，請先對舊版圖標按右鍵，選擇「完全結束」，再按 F5。",
                            "USB Guardian 已在執行",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    Shutdown();
                    return;
                }

                base.OnStartup(e);
                DispatcherUnhandledException += (_, args) =>
                {
                    WriteStartupLog("Unhandled UI exception: " + args.Exception);
                    // Offer an opt-in bug report. Never upload or email diagnostics silently.
                    try
                    {
                        new ReportIssueWindow(args.Exception, "Unhandled UI error").ShowDialog();
                    }
                    catch
                    {
                        try { MessageBox.Show("USB Guardian 發生錯誤。記錄位置：\n" + StartupLogPath,
                            "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Error); }
                        catch { }
                    }
                    args.Handled = true;
                    Shutdown();
                };
                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                    WriteStartupLog("Unhandled process exception: " + args.ExceptionObject);

                WriteStartupLog("Starting USB Guardian (background=" + background + ").");
                UiPreferences.Initialize();
                string notice = UsageConsent.ReadNotice();
                if (!UsageConsent.HasAccepted(notice))
                {
                    UsageNoticeWindow dialog = new(notice);
                    if (dialog.ShowDialog() != true)
                    {
                        WriteStartupLog("First-use notice was declined or closed.");
                        Shutdown();
                        return;
                    }
                    UsageConsent.SaveAcceptance(notice);
                }

                MainWindow window = new MainWindow();
                MainWindow = window;
                if (background)
                {
                    // Keep the intended background monitor/tray behavior.
                    window.InitializeHidden();
                    WriteStartupLog("Started in background mode.");
                }
                else
                {
                    window.Show();
                    WriteStartupLog("Main window shown.");
                }
            }
            catch (Exception ex)
            {
                WriteStartupLog("Startup failed: " + ex);
                try
                {
                    new ReportIssueWindow(ex, "Startup failure").ShowDialog();
                }
                catch
                {
                    try { MessageBox.Show("USB Guardian 無法啟動：\n" + ex.Message +
                        "\n\n錯誤紀錄：" + StartupLogPath,
                        "USB Guardian", MessageBoxButton.OK, MessageBoxImage.Error); }
                    catch { }
                }
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsMutex)
            {
                try { _singleInstance?.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
