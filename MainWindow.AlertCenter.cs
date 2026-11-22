using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using USBGuardian.App.Services;

namespace USBGuardian.App
{
    public partial class MainWindow
    {
        private sealed class SavedAlert
        {
            public SavedAlert() { }
            public string Id { get; set; } = "";
            public DateTime TimeUtc { get; set; }
            public string Title { get; set; } = "";
            public string Details { get; set; } = "";
            public string Drive { get; set; } = "";
            public bool Attention { get; set; }
            public bool Read { get; set; }
            public override string ToString() =>
                $"{(Read ? "" : "●  ")}{TimeUtc.ToLocalTime():MM/dd HH:mm}   {Title}";
        }
        private readonly List<SavedAlert> _savedAlerts = new();
        private static string AlertPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USBGuardian", "alerts.json");

        private void LoadAlertHistory()
        {
            try
            {
                if (File.Exists(AlertPath))
                {
                    var rows = JsonSerializer.Deserialize<List<SavedAlert>>(File.ReadAllText(AlertPath));
                    if (rows != null) _savedAlerts.AddRange(rows.Where(x =>
                        x != null && Guid.TryParseExact(x.Id, "N", out _)).Take(150));
                }
                UpdateAlertList();
            }
            catch (Exception ex)
            {
                Log("無法讀取提醒清單：" + ex.Message);
            }
        }
        private SavedAlert RecordAlert(string title, string message, bool attention, string? drive)
        {
            // A burst of PnP events should not flood the user.
            SavedAlert? last = _savedAlerts.FirstOrDefault();
            if (last != null && !last.Read && last.Title == title && last.Drive == (drive ?? "") &&
                DateTime.UtcNow - last.TimeUtc < TimeSpan.FromSeconds(12)) return last;
            SavedAlert saved = new SavedAlert
            {
                Id = Guid.NewGuid().ToString("N"), TimeUtc = DateTime.UtcNow,
                Title = title, Details = message.Length > 2000 ? message[..2000] : message,
                Drive = drive ?? "", Attention = attention
            };
            _savedAlerts.Insert(0, saved);
            if (_savedAlerts.Count > 150) _savedAlerts.RemoveRange(150, _savedAlerts.Count - 150);
            SaveAlerts();
            UpdateAlertList();
            UpdateHomeSummary();
            return saved;
        }
        private void SaveAlerts()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AlertPath)!);
                string temp = AlertPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_savedAlerts,
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, AlertPath, overwrite: true);
            }
            catch (Exception ex) { Log("提醒清單無法儲存：" + ex.Message); }
        }
        private void UpdateAlertList()
        {
            if (AlertList == null) return;
            string? keep = (AlertList.SelectedItem as SavedAlert)?.Id;
            AlertList.ItemsSource = null;
            AlertList.ItemsSource = _savedAlerts.ToList();
            if (keep != null)
                AlertList.SelectedItem = _savedAlerts.FirstOrDefault(x => x.Id == keep);
            AlertSummaryText.Text = UiPreferences.Runtime(
                $"待處理提醒：{_savedAlerts.Count(x => !x.Read)} 則",
                $"Pending alerts: {_savedAlerts.Count(x => !x.Read)}");
        }
        private void ReviewAlert_Click(object sender, RoutedEventArgs e)
        {
            if (AlertList.SelectedItem is not SavedAlert row)
            {
                MessageBox.Show(UiPreferences.Runtime("請先選擇一則提醒。", "Select an alert first."));
                return;
            }
            row.Read = true;
            SaveAlerts();
            UpdateAlertList();
            MainTabs.SelectedIndex = !string.IsNullOrWhiteSpace(row.Drive) &&
                DriveCombo.Items.Contains(row.Drive) ? 1 : 2;
            UpdateNavAppearance();
            if (MainTabs.SelectedIndex == 1) DriveCombo.SelectedItem = row.Drive;
            UpdateHomeSummary();
        }
        private void MarkAlertRead_Click(object sender, RoutedEventArgs e)
        {
            if (AlertList.SelectedItem is not SavedAlert row) return;
            row.Read = true;
            SaveAlerts();
            UpdateAlertList();
            UpdateHomeSummary();
        }
    }
}
