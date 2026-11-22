using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using Forms = System.Windows.Forms;
using USBGuardian.App.Services;
using USBGuardian.App.UI;

namespace USBGuardian.App
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const long DBT_DEVNODES_CHANGED = 0x0007;
        private const long DBT_DEVICEARRIVAL = 0x8000;
        private const long DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVTYP_VOLUME = 2;
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValue = "USBGuardian";

        private bool _initialized, _quitting, _startupUpdating, _deviceProbeBusy, _deviceProbePending;
        private bool _defenderBusy, _safeLabBusy;
        private HwndSource? _windowSource;
        private Forms.NotifyIcon? _tray;
        private System.Drawing.Icon? _trayIcon;
        private NotificationWindow? _toast;
        private DispatcherTimer? _toastTimer;
        private readonly DispatcherTimer _pnpTimer = new() { Interval = TimeSpan.FromMilliseconds(1350) };
        private readonly HashSet<string> _observedDrives = new(StringComparer.OrdinalIgnoreCase);
        // Only confirmed USB-bus volumes are added when Windows reports them as fixed disks.
        // We never expose local/internal partitions through the normal USB file picker.
        private readonly HashSet<string> _usbDiskLetters = new(StringComparer.OrdinalIgnoreCase);
        private bool _usbEvidenceBusy, _usbEvidencePending;
        private string? _drivePickerIdentity;
        private HashSet<string> _previousPnpIds = new(StringComparer.OrdinalIgnoreCase);
        private bool _pnpBaseline;
        // Keep new observations for the current attachment, not just one refresh.
        private readonly HashSet<string> _sessionAddedPnpIds =
            new(StringComparer.OrdinalIgnoreCase);
        private List<DeviceItem> _devices = new();
        private readonly List<string> _logEvents = new();
        private readonly HashSet<string> _highAlertKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _busyAutoDrives = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FamiliarRecord> _familiar = new(StringComparer.OrdinalIgnoreCase);
        private bool _hasLoadedFamiliar;
        private int _warningCount;
        private string? _currentDrive;
        private FileEntry? _selectedFile;
        // A destructive action is permitted only for the exact file successfully previewed.
        private string? _reviewedFilePath;
        private string? _reviewedFileHash;
        private readonly CancellationTokenSource _closingToken = new();

        private sealed class FamiliarRecord
        {
            public string Label { get; set; } = "";
            public string Category { get; set; } = "";
            public DateTime RecordedUtc { get; set; }
        }
        private static string FamiliarPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USBGuardian", "familiar.json");

        public MainWindow()
        {
            InitializeComponent();
            InitializePreferenceControls();
            SourceInitialized += (_, _) =>
            {
                _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _windowSource?.AddHook(WndProc);
            };
            Loaded += (_, _) => StartServices();
            Closing += (_, e) =>
            {
                if (_quitting) return;
                e.Cancel = true;
                Hide();
            };
            Closed += (_, _) =>
            {
                StopPreferenceControls();
                _closingToken.Cancel();
                _pnpTimer.Stop();
                _windowSource?.RemoveHook(WndProc);
                _tray?.Dispose();
                _trayIcon?.Dispose();
                _toastTimer?.Stop();
                _toast?.Close();
                _closingToken.Dispose();
            };
            _pnpTimer.Tick += (_, _) => { _pnpTimer.Stop(); _ = RefreshDeviceListAsync(); };
        }

        // The background startup path creates a hidden HWND so WM_DEVICECHANGE
        // arrives without displaying the dashboard during login.
        public void InitializeHidden()
        {
            new WindowInteropHelper(this).EnsureHandle();
            StartServices();
        }

        private void StartServices()
        {
            if (_initialized) return;
            _initialized = true;
            InitializeTray();
            LoadFamiliar();
            LoadDeviceProtectionRecords();
            LoadAlertHistory();
            RefreshQuarantineList();
            RefreshDriveCombo();
            RequestExternalDriveEvidence();
            Log("USB Guardian 已啟動，等待 Windows 裝置事件");
            StatusText.Text = UiPreferences.Runtime("USB 插入提醒已啟用", "USB connection alerts are on");
            UpdateHomeSummary();
            UpdateMonitoringIndicator();
            SubtitleText.Text = UiPreferences.Runtime(
                "沒有裝置事件時，不主動掃描檔案或持續查詢裝置。",
                "No continuous scanning when devices are idle.");
            _startupUpdating = true;
            StartupCheck.IsChecked = IsStartupEnabled();
            _startupUpdating = false;
            bool background = Environment.GetCommandLineArgs().Contains("--background");
            _ = StartupDeviceProbeAsync(background);
        }

        private async Task StartupDeviceProbeAsync(bool delayed)
        {
            try
            {
                if (delayed) await Task.Delay(20000, _closingToken.Token);
                await RefreshDeviceListAsync();
            }
            catch (OperationCanceledException) { }
        }

        private void InitializeTray()
        {
            Forms.ContextMenuStrip menu = new();
            menu.Items.Add(UiPreferences.Runtime("開啟 USB Guardian", "Open USB Guardian"), null, (_, _) => Dispatcher.Invoke(OpenDashboard));
            menu.Items.Add(UiPreferences.Runtime("測試通知", "Test notification"), null, (_, _) =>
                Dispatcher.Invoke(() => ShowToast("USB Guardian",
                    UiPreferences.Runtime("通知正常，背景監控已啟動。", "Notifications are working. Monitoring is active."), false, recordAlert: false)));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(UiPreferences.Runtime("完全結束", "Exit USB Guardian"), null, (_, _) => Dispatcher.Invoke(Quit));
            try
            {
                _trayIcon = new System.Drawing.Icon(
                    Path.Combine(AppContext.BaseDirectory, "Assets", "USBGuardian.ico"));
            }
            catch
            {
                // The monitoring service must still start if a published icon file is missing.
                _trayIcon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            }
            _tray = new Forms.NotifyIcon
            {
                Icon = _trayIcon,
                Text = "USB Guardian · 安全監控中",
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenDashboard);
        }
        private void OpenDashboard()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        private void Quit()
        {
            _quitting = true;
            System.Windows.Application.Current.Shutdown();
        }
        private void NavigateSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                int.TryParse(button.Tag?.ToString(), out int index) &&
                index >= 0 && index < MainTabs.Items.Count)
            {
                MainTabs.SelectedIndex = index;
                UpdateNavAppearance();
            }
        }

        private void TestNotification_Click(object sender, RoutedEventArgs e) =>
            ShowToast("USB Guardian", UiPreferences.Runtime(
                "這是通知測試。插入 USB 時也會顯示類似提示。",
                "This is a test. Similar notifications appear when a device connects."), false, recordAlert: false);

        // These are visual previews only: they do not represent a real detection,
        // create an alert, initiate a scan or affect the warning counter.
        private void TestCautionNotification_Click(object sender, RoutedEventArgs e) =>
            ShowToast(UiPreferences.Runtime("測試：有新磁碟插入", "Test: new USB drive"),
                UiPreferences.Runtime("這是注意等級的通知樣式測試，並非實際偵測到風險。",
                    "This is a caution-level visual test, not a detected threat."),
                attention: true, countWarning: false, recordAlert: false);

        private void TestCriticalNotification_Click(object sender, RoutedEventArgs e) =>
            ShowToast(UiPreferences.Runtime("測試：需要處理的裝置", "Test: device needs action"),
                UiPreferences.Runtime("這是高優先提醒的外觀測試，並非病毒警報。請按右上角關閉。",
                    "This is a high-priority visual test, not a malware alert. Dismiss it to close."),
                attention: true, countWarning: false, recordAlert: false,
                severity: NotificationSeverity.Critical);

        // Hidden under Settings > Notification tests. A deliberately long message verifies
        // mouse-wheel scrolling and layout without creating a fake incident.
        private void TestLongNotification_Click(object sender, RoutedEventArgs e) =>
            ShowToast(UiPreferences.Runtime("測試：通知內文閱讀", "Test: notification readability"),
                UiPreferences.Runtime(
                    "這是一則用來檢查通知閱讀方式的測試訊息。\n\n" +
                    "第一段：通知應先說明發生的事，避免使用不明確的安全術語。\n\n" +
                    "第二段：內容太長時，可以在通知內使用滑鼠滾輪查看。\n\n" +
                    "第三段：需要完整資料時，請到活動紀錄查看；測試訊息不會被當成真正的安全事件。",
                    "This is a test of long notification text.\n\n" +
                    "First: A notification should describe the event plainly.\n\n" +
                    "Second: Use the mouse wheel to read a longer message.\n\n" +
                    "Third: The Activity page retains real events. This test does not create a security incident."),
                attention: true, countWarning: false, recordAlert: false);

        private void ShowToast(string title, string text, bool attention, string? scanDrive = null,
            bool countWarning = true, bool recordAlert = true,
            NotificationSeverity? severity = null)
        {
            NotificationSeverity level = severity ?? (attention
                ? NotificationSeverity.Caution : NotificationSeverity.Information);
            // Keep the exact persisted event, so View details never opens an unrelated alert.
            SavedAlert? toastAlert = recordAlert
                ? RecordAlert(title, text, attention || level == NotificationSeverity.Critical, scanDrive)
                : null;
            if (attention && countWarning)
            {
                _warningCount++;
                WarningCountText.Text = _warningCount.ToString();
                UpdateHomeSummary();
            }

            // A lower-priority event must not conceal an urgent notification.
            // The new event is still retained in the history/alert center above.
            if (_toast is { IsVisible: true, Severity: NotificationSeverity.Critical } &&
                level != NotificationSeverity.Critical) return;

            _toastTimer?.Stop();
            _toast?.Close();
            NotificationWindow window = new(title, text, level, () =>
            {
                OpenDashboard();
                // Open the exact saved record. Testing a toast must not fabricate history.
                MainTabs.SelectedIndex = toastAlert == null ? 5 : 4;
                UpdateNavAppearance();
                if (toastAlert != null)
                {
                    // Viewing the full event counts as reading it.
                    toastAlert.Read = true;
                    SaveAlerts();
                    UpdateAlertList();
                    AlertList.SelectedItem = toastAlert;
                    AlertList.ScrollIntoView(toastAlert);
                    UpdateHomeSummary();
                }
            }, scanDrive == null ? null : () => _ = StartDefenderScanAsync(scanDrive));
            _toast = window;
            UpdateMonitoringIndicator();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, window)) _toast = null;
                UpdateMonitoringIndicator();
            };
            window.Show();
            UpdateMonitoringIndicator();
            // User-designated untrusted hardware stays visible until dismissed.
            // Other notifications are persisted in the alert center and expire quietly.
            if (level == NotificationSeverity.Critical) return;
            DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromSeconds(level == NotificationSeverity.Caution ? 16 : 9)
            };
            _toastTimer = timer;
            // If the user is reading or scrolling, do not dismiss the message.
            window.MouseEnter += (_, _) => timer.Stop();
            window.MouseLeave += (_, _) => { if (window.IsVisible) timer.Start(); };
            window.Closed += (_, _) => timer.Stop();
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (window.IsVisible) window.Close();
                if (ReferenceEquals(_toast, window)) _toast = null;
            };
            timer.Start();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_DEVICECHANGE) return IntPtr.Zero;
            long kind = wParam.ToInt64();
            if (kind == DBT_DEVNODES_CHANGED)
            {
                _pnpTimer.Stop();
                _pnpTimer.Start();
                return IntPtr.Zero;
            }
            if (lParam == IntPtr.Zero || Marshal.ReadInt32(lParam) < 18 ||
                Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_VOLUME) return IntPtr.Zero;
            if (kind != DBT_DEVICEARRIVAL && kind != DBT_DEVICEREMOVECOMPLETE) return IntPtr.Zero;
            uint mask = unchecked((uint)Marshal.ReadInt32(lParam, 12));
            for (int i = 0; i < 26; i++)
            {
                if ((mask & (1u << i)) == 0) continue;
                string root = $"{(char)('A' + i)}:\\";
                if (kind == DBT_DEVICEARRIVAL) _ = OnDriveArrivalAsync(root);
                else
                {
                    _observedDrives.Remove(root);
                    _highAlertKeys.RemoveWhere(x => x.StartsWith(root, StringComparison.OrdinalIgnoreCase));
                    Log("磁碟區已移除：" + root);
                    if (string.Equals(_currentDrive, root, StringComparison.OrdinalIgnoreCase))
                    {
                        _drivePickerIdentity = null;
                        ClearFileView(UiPreferences.Runtime("磁碟已移除；請重新選擇 USB。",
                            "Drive removed. Select a connected USB drive."));
                    }
                }
            }
            RefreshDriveCombo();
            RequestExternalDriveEvidence();
            return IntPtr.Zero;
        }

        // Coalesce bursts of volume events. This runs only on startup/device changes,
        // not in an idle polling loop. Windows may expose an external SSD as Fixed.
        private void RequestExternalDriveEvidence()
        {
            _usbEvidencePending = true;
            if (!_usbEvidenceBusy) _ = RefreshExternalDriveEvidenceAsync();
        }

        private async Task RefreshExternalDriveEvidenceAsync()
        {
            if (_usbEvidenceBusy) return;
            _usbEvidenceBusy = true;
            try
            {
                do
                {
                    _usbEvidencePending = false;
                    await Task.Delay(700, _closingToken.Token);
                    IReadOnlyCollection<string> external = await UsbDriveDiscovery.FindUsbBusVolumesAsync(_closingToken.Token);
                    _usbDiskLetters.Clear();
                    foreach (string letter in external) _usbDiskLetters.Add(letter);
                    RefreshDriveCombo();
                }
                while (_usbEvidencePending && !_closingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log("USB 磁碟來源查詢未完成：" + e.Message); }
            finally { _usbEvidenceBusy = false; }
        }

        private void RefreshDriveCombo()
        {
            // A storage volume being readable does not make it a USB volume.
            // DriveType.Removable covers standard flash drives; BusType USB covers
            // USB SSDs that Windows describes as Fixed. Internal C:/D: stay hidden.
            string? previous = DriveCombo.SelectedItem as string;
            string? systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
            var available = new List<string>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady ||
                            string.Equals(drive.Name, systemVolume, StringComparison.OrdinalIgnoreCase)) continue;
                        if (drive.DriveType == DriveType.Removable || _usbDiskLetters.Contains(drive.Name))
                            available.Add(drive.Name);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            available.Sort(StringComparer.OrdinalIgnoreCase);
            // Do not close an open dropdown or discard the selection for unrelated PnP events.
            bool sameVisibleList = available.Count > 0 &&
                DriveCombo.Items.OfType<string>().SequenceEqual(available, StringComparer.OrdinalIgnoreCase);
            if (!sameVisibleList)
            {
                DriveCombo.Items.Clear();
                foreach (string root in available) DriveCombo.Items.Add(root);
                if (available.Count == 0)
                    DriveCombo.Items.Add(UiPreferences.Runtime("尚未連接 USB", "No USB connected"));
                if (previous != null && available.Contains(previous, StringComparer.OrdinalIgnoreCase))
                    DriveCombo.SelectedItem = previous;
                else DriveCombo.SelectedIndex = 0;
            }
            bool hasUsb = available.Count != 0;
            if (!hasUsb && _drivePickerIdentity != null)
            {
                _drivePickerIdentity = null;
                ClearFileView(UiPreferences.Runtime("沒有可檢查的 USB 磁碟。", "No USB drive to inspect."));
            }
            InspectDriveButton.IsEnabled = hasUsb;
            DefenderScanButton.IsEnabled = hasUsb;
            DriveCombo.IsEnabled = hasUsb;
            DriveEmptyText.Visibility = hasUsb ? Visibility.Collapsed : Visibility.Visible;
            DriveCountText.Text = available.Count.ToString();
        }

        private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveCombo.SelectedItem is not string root || !IsExternalUsbRoot(root)) return;
            if (string.Equals(_drivePickerIdentity, root, StringComparison.OrdinalIgnoreCase)) return;
            _drivePickerIdentity = root;
            ClearFileView(UiPreferences.Runtime("選擇了磁碟 " + root + "。按「檢視磁碟」查看檔案。",
                "Selected " + root + ". Choose Browse drive to view files."));
        }

        private bool IsExternalUsbRoot(string drive) =>
            DriveCombo.Items.Contains(drive) &&
            System.Text.RegularExpressions.Regex.IsMatch(drive, @"^[A-Za-z]:\\$") &&
            !string.Equals(drive, Path.GetPathRoot(Environment.SystemDirectory),
                StringComparison.OrdinalIgnoreCase);

        private void ClearFileView(string message)
        {
            if (FileTree == null) return; // Ignore early XAML initialization events.
            _currentDrive = null;
            _selectedFile = null;
            _reviewedFilePath = null;
            _reviewedFileHash = null;
            DeletePreviewedFileButton.IsEnabled = false;
            FileTree.Items.Clear();
            FileDetailsText.Text = UiPreferences.Runtime("尚未選擇檔案。", "No file selected.");
            FileSummaryText.Text = message;
            ScanOutcomeText.Text = UiPreferences.Runtime("尚未針對此磁碟取得 Defender 結果。",
                "No Defender result for this drive.");
            SafeLabFileNameText.Text = UiPreferences.Runtime("尚未選取檔案", "No file selected");
            SafeLabStatusText.Text = UiPreferences.Runtime("請先選擇檔案，再執行預覽。",
                "Select a file before previewing.");
            SafeLabPreviewImage.Source = null;
            SafeLabPreviewImage.Visibility = Visibility.Collapsed;
            SafeLabTextPanel.Visibility = Visibility.Collapsed;
            SafeLabTextText.Text = "";
            SafeLabPreviewHint.Visibility = Visibility.Visible;
            SafeLabPreviewHint.Text = UiPreferences.Runtime("從上方清單選擇檔案，再按「預覽所選檔案」。",
                "Choose a file from the list, then select Preview selected file.");
            SafeLabMismatchPanel.Visibility = Visibility.Collapsed;
            SafeLabKindText.Text = UiPreferences.Runtime("等待選擇檔案", "Select a file first");
            SafeLabKindExplainText.Text = "";
            SafeLabHashText.Text = "—";
            SafeLabHexText.Text = "";
        }

        private async Task OnDriveArrivalAsync(string drive)
        {
            if (!_observedDrives.Add(drive)) return; // dedupe repeated Windows volume events
            Log("偵測到磁碟區接入：" + drive);
            ShowToast(
                UiPreferences.Runtime("注意：有新磁碟插入", "Attention: new drive connected"),
                UiPreferences.Runtime(
                    $"偵測到 {drive}。可能有安全風險，請先檢查內容，再決定是否使用。",
                    $"Drive {drive} was connected. It may pose a security risk. Inspect it before use."),
                attention: true, scanDrive: drive, countWarning: false);
            if (!_busyAutoDrives.Add(drive)) return;
            try
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    await Task.Delay(500);
                    if (!_observedDrives.Contains(drive)) return;
                    DriveInfo info = new(drive);
                    if (!info.IsReady) continue;
                    if (info.DriveType != DriveType.Removable)
                    {
                        Log($"{drive} 類型為 {info.DriveType}，略過自動檔案檢查");
                        return;
                    }
                    FolderSnapshot snapshot = await Task.Run(() => FileInspection.List(drive, 180));
                    if (!_observedDrives.Contains(drive)) return;
                    if (snapshot.Error != null) { Log($"快檢失敗：{snapshot.Error}"); return; }
                    List<FileEntry> concerns = snapshot.Entries.Where(FileInspection.HighAttention).ToList();
                    Log($"{drive} 自動快檢根目錄 {snapshot.Entries.Count} 項，高關注特徵 {concerns.Count} 項" +
                        (snapshot.Truncated ? "（未完整列出）" : ""));
                    if (concerns.Count > 0 && _highAlertKeys.Add(drive + string.Join("|", concerns.Select(c => c.Name))))
                        ShowToast(
                            UiPreferences.Runtime("檔名需要確認", "File name needs review"),
                            UiPreferences.Runtime(
                                $"{drive}：{concerns[0].Name}。這個檔案可能有安全風險，建議先掃描。",
                                $"{drive}: {concerns[0].Name}. This file may pose a security risk. Scan it before opening."),
                            true, drive);
                    return;
                }
                Log("略過：磁碟尚未準備好 " + drive);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { Log("USB 快檢未完成：" + ex.Message); }
            finally { _busyAutoDrives.Remove(drive); }
        }

        private async Task RefreshDeviceListAsync()
        {
            if (_deviceProbeBusy) { _deviceProbePending = true; return; }
            _deviceProbeBusy = true;
            DeviceStatusText.Text = "正在查詢 Windows 裝置（不掃描檔案）...";
            try
            {
                List<DeviceItem> devices = await DeviceProbe.QueryAsync(_closingToken.Token);
                HashSet<string> ids = devices.Select(x => x.InstanceId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                List<DeviceItem> added = _pnpBaseline
                    ? devices.Where(x => !_previousPnpIds.Contains(x.InstanceId)).ToList()
                    : new List<DeviceItem>();
                foreach (DeviceItem item in added)
                    _sessionAddedPnpIds.Add(item.InstanceId);
                _sessionAddedPnpIds.RemoveWhere(id => !ids.Contains(id));
                foreach (DeviceItem item in devices)
                {
                    item.IsNewObservation = _sessionAddedPnpIds.Contains(item.InstanceId);
                    item.IsFamiliar = _familiar.TryGetValue(Fingerprint(item.InstanceId), out FamiliarRecord? record);
                    item.IsUntrusted = _untrustedDevices.ContainsKey(Fingerprint(item.InstanceId));
                    item.HadFunctionChange = item.IsFamiliar && record != null &&
                        !string.Equals(record.Category, item.Category, StringComparison.OrdinalIgnoreCase);
                }
                _devices = devices;
                _previousPnpIds = ids;
                _pnpBaseline = true;
                var sorted = devices.OrderBy(x => x.OriginOrder)
                    .ThenBy(x => x.IsUntrusted ? 0 : x.AttentionLevel >= 2 ? 1 :
                        x.AttentionLevel == 1 ? 2 : x.IsFamiliar ? 3 : 4)
                    .ThenBy(x => x.Category).ThenBy(x => x.Name).ToList();
                ICollectionView view = CollectionViewSource.GetDefaultView(sorted);
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceItem.LocalizedOriginBucket)));
                view.Filter = item => MatchesSelectedCategory(item as DeviceItem);
                DeviceList.ItemsSource = view;
                RefreshActionDeviceChoices();
                UpdateDeviceCategorySummary();
                UpdateDisabledDeviceChoices();
                DeviceCountText.Text = devices.Count.ToString();
                UpdateHomeSummary();
                DeviceStatusText.Text = UiPreferences.Runtime(
                    "已更新裝置清單。部分裝置可能會顯示多個項目。",
                    "Device list updated. Some hardware may appear more than once.");
                if (added.Count > 0)
                {
                    Log($"PnP 新增 {added.Count} 筆裝置節點");
                    DeviceItem? first = devices.Where(x => x.IsNewObservation && !x.IsSystemComponent)
                        .OrderByDescending(x => x.IsUntrusted)
                        .ThenByDescending(x => x.AttentionLevel).FirstOrDefault();
                    if (first != null)
                        ShowToast(
                            first.IsUntrusted
                                ? UiPreferences.Runtime("已標記不信任的裝置再次出現", "Previously untrusted device detected")
                                : UiPreferences.Runtime("偵測到新裝置", "New device detected"),
                            first.IsUntrusted
                                ? UiPreferences.Runtime(
                                    $"{first.Name} 已被你標記為不信任。請確認裝置狀態，避免直接使用。",
                                    $"You previously marked {first.Name} as untrusted. Review it before use.")
                                : UiPreferences.Runtime(
                                    $"{first.Category} · {first.Name}。{first.SuggestedAction}",
                                    $"{first.Name}. Open devices to review its function."),
                            first.IsUntrusted || first.AttentionLevel == 2,
                            severity: first.IsUntrusted ? NotificationSeverity.Critical
                                : first.AttentionLevel >= 1 ? NotificationSeverity.Caution
                                : NotificationSeverity.Information);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { DeviceStatusText.Text = "查詢失敗：" + ex.Message; Log("PnP 查詢失敗：" + ex.Message); }
            finally
            {
                _deviceProbeBusy = false;
                if (_deviceProbePending) { _deviceProbePending = false; _pnpTimer.Stop(); _pnpTimer.Start(); }
            }
        }
        private void RefreshDevices_Click(object sender, RoutedEventArgs e) => _ = RefreshDeviceListAsync();

        private bool MatchesSelectedCategory(DeviceItem? item)
        {
            if (item == null) return false;
            string origin = (DeviceOriginFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "全部來源";
            if (origin != "全部來源" && item.OriginBucket != origin) return false;
            string category = (DeviceCategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                              ?? "全部類別";
            if (category != "全部類別" && item.Category != category) return false;
            string attention = (DeviceAttentionFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                               ?? "全部狀態";
            if (attention == "需要確認" && item.AttentionLevel < 2) return false;
            if (attention == "新觀察到" && !item.IsNewObservation) return false;
            if (attention == "有熟悉紀錄" && !item.IsFamiliar) return false;
            if (attention == "已標記不信任" && !item.IsUntrusted) return false;
            if (DeviceHideSystemCheck.IsChecked == true && item.IsSystemComponent) return false;
            string q = DeviceSearchBox.Text.Trim();
            return q.Length == 0 || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
        private void DeviceOriginFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDeviceFilters();
        private void DeviceCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDeviceFilters();
        private void DeviceAttentionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDeviceFilters();
        private void DeviceHideSystemCheck_Changed(object sender, RoutedEventArgs e) => RefreshDeviceFilters();
        private void DeviceSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshDeviceFilters();
        private void RefreshDeviceFilters()
        {
            if (DeviceList == null || DeviceOriginFilter == null || DeviceCategoryFilter == null || DeviceAttentionFilter == null ||
                DeviceSearchBox == null || DeviceHideSystemCheck == null) return;
            if (DeviceList.ItemsSource is ICollectionView view) view.Refresh();
            UpdateDeviceCategorySummary();
        }
        private void UpdateDeviceCategorySummary()
        {
            if (DeviceCategorySummary == null || DeviceOverviewText == null || DeviceOriginFilter == null || DeviceAttentionFilter == null ||
                DeviceSearchBox == null || DeviceHideSystemCheck == null || DeviceCategoryFilter == null) return;
            int count = _devices.Count(MatchesSelectedCategory);
            int urgent = _devices.Count(x => x.AttentionLevel == 2);
            int recently = _devices.Count(x => x.IsNewObservation && !x.IsSystemComponent);
            int untrusted = _devices.Count(x => x.IsUntrusted);
            DeviceOverviewText.Text = UiPreferences.Runtime(
                $"需確認 {urgent} 項  ·  本次新增 {recently} 項  ·  你標記不信任 {untrusted} 項",
                $"Needs review {urgent} · New entries {recently} · Marked untrusted {untrusted}");
            DeviceCategorySummary.Text = UiPreferences.Runtime(
                $"顯示 {count} 個項目", $"{count} entries shown");
            if (DeviceEmptyStateText != null)
                DeviceEmptyStateText.Visibility = count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }


        private async void InspectDrive_Click(object sender, RoutedEventArgs e)
        {
            if (DriveCombo.SelectedItem is not string root) { System.Windows.MessageBox.Show("請先選擇磁碟。"); return; }
            try
            {
                DriveInfo drive = new(root);
                if (!drive.IsReady) throw new IOException("磁碟尚未準備好");
                if (drive.DriveType != DriveType.Removable &&
                    System.Windows.MessageBox.Show("所選磁碟不是 Windows 標記的可攜磁碟。仍要唯讀檢視嗎？",
                        "確認磁碟", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _currentDrive = root;
                _selectedFile = null;
                _reviewedFilePath = null;
                if (DeletePreviewedFileButton != null) DeletePreviewedFileButton.IsEnabled = false;
                FileTree.Items.Clear();
                FileDetailsText.Text = "選擇左側項目查看屬性。";
                FileSummaryText.Text = "正在檢視根目錄...";
                TreeViewItem node = CreateTreeNode(new FileEntry { Path = root, Name = root, IsDirectory = true });
                FileTree.Items.Add(node);
                await LoadFolderNodeAsync(node);
                node.IsExpanded = true;
            }
            catch (Exception ex) { FileSummaryText.Text = "無法檢視：" + ex.Message; }
        }
        private TreeViewItem CreateTreeNode(FileEntry entry)
        {
            string label = entry.Risk.Length == 0 ? entry.ToString() :
                UiPreferences.Runtime("⚠ 可能有風險 · " + entry.Name, "⚠ Needs review · " + entry.Name);
            TreeViewItem node = new() { Tag = entry, Header = label };
            if (entry.Risk.Length != 0)
                node.SetResourceReference(TreeViewItem.ForegroundProperty, "WarningTextBrush");
            if (entry.IsDirectory && !entry.IsReparsePoint)
            {
                node.Items.Add("載入中...");
                node.Expanded += async (_, e) =>
                {
                    if (!ReferenceEquals(node, e.OriginalSource)) return;
                    await LoadFolderNodeAsync(node);
                };
            }
            return node;
        }
        private async Task LoadFolderNodeAsync(TreeViewItem node)
        {
            if (node.Tag is not FileEntry item || item.HasLoadedChildren || item.IsReparsePoint) return;
            item.HasLoadedChildren = true;
            node.Items.Clear();
            node.Items.Add("讀取中...");
            FolderSnapshot snapshot = await Task.Run(() => FileInspection.List(item.Path));
            if (!node.IsLoaded && node.Parent == null && !FileTree.Items.Contains(node)) return;
            node.Items.Clear();
            if (snapshot.Error != null) { node.Items.Add("讀取失敗：" + snapshot.Error); return; }
            // High-concern entries always come first, even above ordinary folders.
            // Apply this independently to every expanded folder, not just the drive root.
            foreach (FileEntry child in snapshot.Entries
                .OrderByDescending(FileInspection.HighAttention)
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.Risk))
                .ThenByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                node.Items.Add(CreateTreeNode(child));
            if (snapshot.Truncated) node.Items.Add("[超過 240 項，本層未完整檢視]");
            if (snapshot.Entries.Count == 0) node.Items.Add("（空資料夾）");
            FileSummaryText.Text = $"本層 {snapshot.Entries.Count} 項 · {snapshot.RiskCount} 項需留意（不含未展開的子資料夾）";
            Log("已檢視資料夾：" + item.Path);
        }
        private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not TreeViewItem node || node.Tag is not FileEntry item) return;
            _selectedFile = item;
            _reviewedFilePath = null;
            _reviewedFileHash = null;
            if (DeletePreviewedFileButton != null) DeletePreviewedFileButton.IsEnabled = false;
            // A different selection invalidates the previously rendered preview as well as its hash.
            SafeLabPreviewImage.Source = null;
            SafeLabPreviewImage.Visibility = Visibility.Collapsed;
            SafeLabTextPanel.Visibility = Visibility.Collapsed;
            SafeLabTextText.Text = "";
            SafeLabMismatchPanel.Visibility = Visibility.Collapsed;
            SafeLabHexText.Text = "";
            SafeLabHashText.Text = "—";
            SafeLabFileNameText.Text = item.Name;
            SafeLabKindText.Text = item.IsDirectory
                ? UiPreferences.Runtime("資料夾", "Folder")
                : UiPreferences.Runtime("尚未預覽", "Not previewed");
            SafeLabKindExplainText.Text = "";
            SafeLabStatusText.Text = item.IsDirectory
                ? UiPreferences.Runtime("請選擇檔案，而不是資料夾。", "Choose a file, not a folder.")
                : UiPreferences.Runtime("已選取檔案，尚未進行預覽。", "File selected; not yet previewed.");
            SafeLabPreviewModeText.Text = UiPreferences.Runtime("內容預覽", "Content preview");
            SafeLabPreviewHint.Visibility = Visibility.Visible;
            SafeLabPreviewHint.Text = item.IsDirectory
                ? UiPreferences.Runtime("從資料夾內選擇一個檔案。", "Select a file inside the folder.")
                : UiPreferences.Runtime("按「預覽所選檔案」查看內容。", "Select Preview selected file to inspect the contents.");
            FileDetailsText.Text = $"名稱：{item.Name}\n路徑：{item.Path}\n" +
                $"類別：{(item.IsDirectory ? "資料夾" : "檔案")}\n隱藏：{(item.Hidden ? "是" : "否")}\n" +
                $"連結：{(item.IsReparsePoint ? "是" : "否")}\n\n" +
                (item.Risk.Length > 0 ? "注意：" + item.Risk : "未發現可疑檔名特徵") +
                "";
        }

        private async void SafeLabAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (_safeLabBusy) return;
            if (_selectedFile is not FileEntry entry || entry.IsDirectory || entry.IsReparsePoint)
            { System.Windows.MessageBox.Show("請先在檔案樹選擇一般檔案（不是資料夾／連結）。"); return; }
            if (_currentDrive == null ||
                !string.Equals(Path.GetPathRoot(entry.Path), _currentDrive, StringComparison.OrdinalIgnoreCase))
            { System.Windows.MessageBox.Show("檔案與目前檢視的磁碟不同。請重新選擇。"); return; }
            // Preview is a read-only, explicitly requested action. No redundant confirmation modal;
            // irreversible actions (quarantine/delete or OS device control) still ask for consent.
            _safeLabBusy = true;
            UpdateMonitoringIndicator();
            _reviewedFilePath = null;
            _reviewedFileHash = null;
            DeletePreviewedFileButton.IsEnabled = false;
            MainTabs.SelectedIndex = 1;
            UpdateNavAppearance();
            SafeLabPreviewSection.BringIntoView();
            SafeLabStatusText.Text = "正在準備預覽…";
            SafeLabHexText.Text = SafeLabTextText.Text = "";
            SafeLabKindText.Text = SafeLabHashText.Text = "";
            SafeLabFileNameText.Text = entry.Name;
            SafeLabKindExplainText.Text = "正在讀取檔案格式…";
            SafeLabPreviewImage.Source = null;
            SafeLabPreviewImage.Visibility = Visibility.Collapsed;
            SafeLabPreviewHint.Visibility = Visibility.Visible;
            SafeLabPreviewHint.Text = "正在檢查檔案…";
            SafeLabTextPanel.Visibility = Visibility.Collapsed;
            SafeLabPreviewModeText.Text = "準備判斷內容格式";
            SafeLabMismatchPanel.Visibility = Visibility.Collapsed;
            SafeLabMismatchText.Text = "";
            try
            {
                SafeLabResult result = await SafeLabService.AnalyzeAsync(entry.Path);
                // A slow preview must not appear under a different file selected meanwhile.
                if (!ReferenceEquals(_selectedFile, entry)) return;
                if (result.SkippedDueToSize)
                {
                    string size = (result.FileSizeBytes / (1024.0 * 1024.0)).ToString("0.0");
                    SafeLabStatusText.Text = UiPreferences.Runtime(
                        $"未預覽：檔案約 {size} MiB，超過 SafeLab 24 MiB 上限。",
                        $"Not previewed: this file is {size} MiB and exceeds SafeLab's 24 MiB limit.");
                    SafeLabPreviewModeText.Text = UiPreferences.Runtime("未進行內容分析", "Content not analyzed");
                    SafeLabKindText.Text = UiPreferences.Runtime("超過預覽大小限制", "Preview size limit exceeded");
                    SafeLabKindExplainText.Text = UiPreferences.Runtime(
                        "未啟動 SafeLab 分析，也未判定檔案是否安全。",
                        "SafeLab was not run. No safety determination was made.");
                    SafeLabPreviewHint.Text = UiPreferences.Runtime(
                        "這個檔案太大，無法使用目前的安全預覽。你可以另外選擇 Defender 掃描。請勿直接開啟來源不明的檔案。",
                        "This file is too large for the current safe preview. You can request a separate Defender scan. Do not open files from unknown sources.");
                    SafeLabImageNote.Text = UiPreferences.Runtime(
                        "超過大小限制不是病毒警報，也不代表檔案安全。",
                        "Exceeding the size limit is not a malware finding or proof of safety.");
                    // A skipped preview is NOT eligible for a post-preview
                    // quarantine/removal action.
                    DeletePreviewedFileButton.IsEnabled = false;
                    Log("SafeLab 未預覽（檔案超過 24 MiB 上限）：" + entry.Name);
                    return;
                }
                SafeLabStatusText.Text = "預覽已完成";
                SafeLabKindText.Text = result.FileKind;
                SafeLabHashText.Text = result.Sha256;
                SafeLabHexText.Text = result.Hex;
                SafeLabTextText.Text = result.Text;
                SafeLabPreviewModeText.Text = result.PreviewMode switch
                {
                    "image" => "圖片預覽",
                    "text" => "純文字預覽",
                    _ => "不提供內容預覽"
                };
                SafeLabImageNote.Text = string.IsNullOrWhiteSpace(result.PreviewNote)
                    ? "分析程序不會執行檔案，也不會使用主系統的檔案關聯程式。"
                    : result.PreviewNote;
                if (result.PreviewWidth > 0 && result.PreviewHeight > 0 &&
                    result.PreviewPixelsBase64.Length > 0)
                {
                    byte[] pixels = Convert.FromBase64String(result.PreviewPixelsBase64);
                    BitmapSource preview = BitmapSource.Create(
                        result.PreviewWidth, result.PreviewHeight, 96, 96,
                        PixelFormats.Bgra32, null, pixels, result.PreviewWidth * 4);
                    preview.Freeze();
                    SafeLabPreviewImage.Source = preview;
                    SafeLabPreviewImage.Visibility = Visibility.Visible;
                    SafeLabPreviewHint.Visibility = Visibility.Collapsed;
                    SafeLabKindExplainText.Text = "圖片預覽已產生。";
                }
                else if (result.PreviewMode == "text")
                {
                    SafeLabPreviewHint.Visibility = Visibility.Collapsed;
                    SafeLabTextPanel.Visibility = Visibility.Visible;
                    SafeLabKindExplainText.Text = "純文字預覽（僅顯示部分內容）。";
                }
                else
                {
                    SafeLabPreviewHint.Text = result.PreviewMode == "image"
                        ? (string.IsNullOrWhiteSpace(result.PreviewNote)
                            ? "圖片解碼失敗，未提供預覽。" : result.PreviewNote)
                        : "此檔案不提供內容預覽。\n我們不會在主系統執行或開啟未知格式。";
                    SafeLabKindExplainText.Text = "這個格式目前不支援直接預覽。";
                }
                // Compare claimed extension with content-derived format.
                // A mismatch warrants checking, but is NOT proof of malware.
                string declared = Path.GetExtension(entry.Name).ToLowerInvariant();
                bool claimedImage = declared is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp";
                bool claimedText = declared is ".txt" or ".csv" or ".md" or ".log";
                bool mismatch = (claimedImage && result.PreviewMode != "image") ||
                                (claimedText && result.PreviewMode == "image") ||
                                (declared == ".exe" && !result.FileKind.StartsWith("Windows 程式檔", StringComparison.Ordinal));
                if (mismatch)
                {
                    SafeLabMismatchText.Text = "請確認副檔名：檔案名稱宣稱的類型與內容判斷不同。" +
                        " 這可能只是重新命名或格式特殊，不代表已確認惡意。";
                    SafeLabMismatchPanel.Visibility = Visibility.Visible;
                }
                _reviewedFilePath = entry.Path;
                _reviewedFileHash = result.Sha256;
                DeletePreviewedFileButton.IsEnabled = true;
                Log("SafeLab 完成：" + entry.Name);
            }
            catch (Exception ex)
            {
                SafeLabStatusText.Text = "隔離分析不可用，已拒絕非隔離執行。原因：" + ex.Message;
                SafeLabPreviewHint.Text = "隔離程序未成功，因此不顯示內容預覽。";
                SafeLabPreviewModeText.Text = "分析未完成";
                Log("SafeLab 失敗（fail-closed）：" + ex.Message);
            }
            finally { _safeLabBusy = false; UpdateMonitoringIndicator(); }
        }

        private void DefenderScan_Click(object sender, RoutedEventArgs e)
        {
            if (DriveCombo.SelectedItem is string drive) _ = StartDefenderScanAsync(drive);
            else System.Windows.MessageBox.Show("請先選擇磁碟。");
        }
        private async Task StartDefenderScanAsync(string drive)
        {
            if (_defenderBusy) return;
            // A notification may outlive its volume: never let a stale button scan an internal drive.
            if (!IsExternalUsbRoot(drive))
            {
                System.Windows.MessageBox.Show(UiPreferences.Runtime(
                    "這個磁碟目前未被確認為外接 USB。請重新選擇已連接的 USB 磁碟。",
                    "This drive is not currently recognized as external USB storage. Select a connected USB drive."));
                return;
            }
            if (System.Windows.MessageBox.Show($"確定使用 Microsoft Defender 掃描 {drive}？\n\n防毒軟體可能隔離檔案。",
                "啟動 Defender", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _defenderBusy = true;
            UpdateMonitoringIndicator();
            ScanOutcomeText.Text = UiPreferences.Runtime(
                $"正在要求 Defender 掃描 {drive}；尚未取得掃描結果。",
                $"Defender scan requested for {drive}; results are pending.");
            StatusText.Text = "Defender 正在掃描：" + drive;
            try
            {
                bool success = await Task.Run(async () =>
                {
                    string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "WindowsPowerShell", "v1.0", "powershell.exe");
                    string script = "$ErrorActionPreference='Stop'; Start-MpScan -ScanType CustomScan -ScanPath '" + drive + "' -ErrorAction Stop";
                    using Process process = Process.Start(new ProcessStartInfo
                    {
                        FileName = ps, Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                          Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                        UseShellExecute = false, CreateNoWindow = true
                    }) ?? throw new IOException("無法啟動 Defender 指令");
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                });
                StatusText.Text = success
                    ? "Defender 掃描命令已結束；請至 Windows 安全性確認結果。"
                    : "Defender 命令未成功，請在 Windows 安全性查看原因。";
                if (string.Equals(DriveCombo.SelectedItem as string, drive, StringComparison.OrdinalIgnoreCase))
                    ScanOutcomeText.Text = success
                    ? UiPreferences.Runtime("掃描命令完成。請在 Windows 安全性中確認是否發現威脅；本程式尚未驗證防毒結果。",
                        "Scan command completed. Check Windows Security for threats; this app has not verified the antivirus verdict.")
                    : UiPreferences.Runtime("Defender 掃描命令失敗；請開啟 Windows 安全性查看原因。",
                        "Defender scan command failed. Open Windows Security for details.");
                Log($"Defender 掃描命令結果：{drive} / {(success ? "完成" : "失敗")}");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Defender 無法完成：" + ex.Message;
                if (string.Equals(DriveCombo.SelectedItem as string, drive, StringComparison.OrdinalIgnoreCase))
                    ScanOutcomeText.Text = UiPreferences.Runtime("Defender 沒有完成：", "Defender did not complete: ") + ex.Message;
            }
            finally { _defenderBusy = false; UpdateMonitoringIndicator(); }
        }

        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceList.SelectedItem is not DeviceItem item)
            {
                if (DeviceDetailsContent != null) DeviceDetailsContent.Visibility = Visibility.Collapsed;
                if (DeviceSelectHint != null) DeviceSelectHint.Visibility = Visibility.Visible;
                DeviceSelectedNameText.Text = "請選擇裝置";
                DeviceRiskText.Text = "尚未選取";
                DeviceOriginText.Text = "尚未選取裝置";
                DeviceProtectionText.Text = "請先選擇左側裝置。";
                DevicePurposeText.Text = "點選左側一筆裝置，了解它的用途。";
                DeviceActionText.Text = "";
                DeviceTechnicalText.Text = "";
                return;
            }
            if (ActionDeviceCombo != null && !ReferenceEquals(ActionDeviceCombo.SelectedItem, item))
                ActionDeviceCombo.SelectedItem = item;
            if (DeviceDetailsContent != null) DeviceDetailsContent.Visibility = Visibility.Visible;
            if (DeviceSelectHint != null) DeviceSelectHint.Visibility = Visibility.Collapsed;
            string key = Fingerprint(item.InstanceId);
            bool familiar = _familiar.TryGetValue(key, out FamiliarRecord? known);
            NicknameBox.Text = familiar ? known!.Label : "";
            DeviceSelectedNameText.Text = familiar && !string.IsNullOrWhiteSpace(known!.Label)
                ? known.Label : item.Name;
            DeviceRiskText.Text = item.LocalizedAttention;
            DeviceRiskText.SetResourceReference(TextBlock.ForegroundProperty,
                item.AttentionLevel >= 1 ? "WarningTextBrush" : "AccentTextBrush");
            DeviceSelectedCategoryText.Text = item.LocalizedCategory;
            DeviceOriginText.Text = item.LocalizedOriginBucket + " · " + item.LocalizedOrigin;
            DeviceProtectionText.Text = _disabledStorageDevices.ContainsKey(item.InstanceId)
                ? UiPreferences.Runtime("曾要求 Windows 停用；目前狀態請在裝置管理員確認。",
                    "Windows disable was requested; check Device Manager for the current state.")
                : item.IsUntrusted
                    ? UiPreferences.Runtime("已列為需注意；尚未停用。", "Marked for attention; not disabled.")
                    : UiPreferences.Runtime("尚未標記。", "Not marked.");
            DevicePurposeText.Text = item.LocalizedPurpose;
            DeviceActionText.Text = item.LocalizedAction;
            DeviceTechnicalText.Text = UiPreferences.Runtime(
                $"Windows 名稱：{item.Name}\nPnP 類別：{item.Class}\n狀態：{item.ShortStatus}\n" +
                $"Instance ID：{item.InstanceId}\n此為 Windows 裝置項目，不代表實體裝置或安全認證。",
                $"Windows name: {item.Name}\nPnP class: {item.Class}\nStatus: {item.ShortStatus}\n" +
                $"Instance ID: {item.InstanceId}\nThese are Windows entries, not verified physical identities.");
        }
        private static string Fingerprint(string id) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        private void MarkFamiliar_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedManagedDevice() is not DeviceItem d) return;
            string key = Fingerprint(d.InstanceId);
            _familiar[key] = new FamiliarRecord
            {
                Label = string.IsNullOrWhiteSpace(NicknameBox.Text) ? d.Name : NicknameBox.Text.Trim(),
                Category = d.Category, RecordedUtc = DateTime.UtcNow
            };
            SaveFamiliar();
            d.IsFamiliar = true;
            d.HadFunctionChange = false;
            RefreshDeviceFilters();
            if (DeviceList.SelectedItem is DeviceItem shown &&
                string.Equals(shown.InstanceId, d.InstanceId, StringComparison.OrdinalIgnoreCase))
                DeviceList_SelectionChanged(sender, new SelectionChangedEventArgs(
                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new object[0], new object[0]));
            UpdateActionDeviceContext();
            Log("已記錄熟悉裝置（非安全認證）：" + d.Name);
        }
        private void UnmarkFamiliar_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedManagedDevice() is not DeviceItem d) return;
            _familiar.Remove(Fingerprint(d.InstanceId));
            SaveFamiliar();
            d.IsFamiliar = false;
            d.HadFunctionChange = false;
            RefreshDeviceFilters();
            if (DeviceList.SelectedItem is DeviceItem shown &&
                string.Equals(shown.InstanceId, d.InstanceId, StringComparison.OrdinalIgnoreCase))
                DeviceList_SelectionChanged(sender, new SelectionChangedEventArgs(
                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new object[0], new object[0]));
        }
        private void LoadFamiliar()
        {
            if (_hasLoadedFamiliar) return;
            _hasLoadedFamiliar = true;
            try
            {
                if (File.Exists(FamiliarPath))
                {
                    var contents = JsonSerializer.Deserialize<Dictionary<string, FamiliarRecord>>(
                        File.ReadAllText(FamiliarPath));
                    if (contents != null) foreach (var pair in contents) _familiar[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex) { Log("無法讀取熟悉裝置紀錄：" + ex.Message); }
        }
        private void SaveFamiliar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FamiliarPath)!);
                File.WriteAllText(FamiliarPath, JsonSerializer.Serialize(_familiar,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log("儲存熟悉裝置失敗：" + ex.Message); }
        }
        private void ExportReport_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog picker = new() { FileName = "USBGuardian-report.txt", Filter = "文字報告|*.txt" };
            if (picker.ShowDialog() != true) return;
            StringBuilder output = new();
            output.AppendLine("USB Guardian 裝置觀察報告（非病毒掃描證明）");
            output.AppendLine("時間：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (DeviceItem item in _devices)
                output.AppendLine($"{item.Category} | {item.Name} | {item.Class} | {item.Status} | {item.OriginLabel} | 不信任標記:{item.IsUntrusted}");
            output.AppendLine("\n本次執行的事件：");
            foreach (string line in _logEvents) output.AppendLine(line);
            try { File.WriteAllText(picker.FileName, output.ToString()); }
            catch (Exception ex) { System.Windows.MessageBox.Show("匯出失敗：" + ex.Message); }
        }
        private void OpenUsageNotice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UsageNoticeWindow dialog = new(UsageConsent.ReadNotice(), readOnly: true)
                { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("無法開啟使用須知：" + ex.Message);
            }
        }

        private void ShowResourceUsage_Click(object sender, RoutedEventArgs e)
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            double memoryMb = process.WorkingSet64 / (1024.0 * 1024.0);
            double cpuSeconds = process.TotalProcessorTime.TotalSeconds;
            ShowToast(
                UiPreferences.Runtime("資源使用狀態", "Resource usage"),
                UiPreferences.Runtime(
                    $"目前記憶體約 {memoryMb:F1} MB；累積 CPU 時間約 {cpuSeconds:F1} 秒。這不是待機 CPU 比例或開機效能測試。",
                    $"Memory {memoryMb:F1} MB; CPU time {cpuSeconds:F1} s. Not an idle CPU or startup benchmark."),
                false);
        }

        private void StartupChanged(object sender, RoutedEventArgs e)
        {
            if (_startupUpdating || !_initialized) return;
            bool enabled = StartupCheck.IsChecked == true;
            try
            {
                string exe = Environment.ProcessPath ?? "";
                if (enabled && (exe.Length == 0 || exe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)))
                {
                    _startupUpdating = true;
                    StartupCheck.IsChecked = false;
                    _startupUpdating = false;
                    System.Windows.MessageBox.Show("請先發布 Release 版本，再啟用開機自動執行。");
                    return;
                }
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(StartupKey);
                if (enabled) key?.SetValue(StartupValue, $"\"{exe}\" --background");
                else key?.DeleteValue(StartupValue, false);
            }
            catch (Exception ex) { Log("自動啟動設定失敗：" + ex.Message); }
        }
        private static bool IsStartupEnabled()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupKey);
            return key?.GetValue(StartupValue) is string;
        }
        private void Log(string text)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            _logEvents.Insert(0, line);
            LogList.Items.Insert(0, line);
            RecentList.Items.Insert(0, BuildActivityRow(line));
            while (_logEvents.Count > 180) _logEvents.RemoveAt(_logEvents.Count - 1);
            while (LogList.Items.Count > 180) LogList.Items.RemoveAt(LogList.Items.Count - 1);
            while (RecentList.Items.Count > 12) RecentList.Items.RemoveAt(RecentList.Items.Count - 1);
        }
    }
}
