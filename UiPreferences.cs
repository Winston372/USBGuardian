using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace USBGuardian.App.Services
{
    // UI-only preferences: no effect on USB inspection or SafeLab permissions.
    internal static class UiPreferences
    {
        private sealed class Preferences
        {
            public string Language { get; set; } = "zh-TW";
            public string Theme { get; set; } = "system";
        }

        public static string Language { get; private set; } =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-TW" : "en";
        public static string Theme { get; private set; } = "system";

        private static string SettingsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USBGuardian", "appearance.json");

        private static readonly Dictionary<string, string> Light = new()
        {
            ["CanvasBrush"] = "#F8F9FA",
            ["SurfaceBrush"] = "#FFFFFF",
            ["SubtleBrush"] = "#F3F4F6",
            ["InputBrush"] = "#FFFFFF",
            ["TextBrush"] = "#202329",
            ["MutedTextBrush"] = "#5E6570",
            ["BorderBrush"] = "#DEE2E7",
            ["AccentBrush"] = "#155EA2",
            ["AccentTextBrush"] = "#155EA2",
            ["SoftButtonBrush"] = "#F2F4F6",
            ["SoftButtonTextBrush"] = "#2D4359",
            ["WarningLightBrush"] = "#FCF1E0",
            ["WarningTextBrush"] = "#945908",
            ["SuccessLightBrush"] = "#E7F4EE",
            ["ToastBackgroundBrush"] = "#FFFFFF",
            ["ToastBorderBrush"] = "#D4DDE7",
            ["ToastTextBrush"] = "#202329",
            ["NotificationInfoBrush"] = "#2B669C",
            ["NotificationInfoTintBrush"] = "#E8F0F8",
            ["NotificationCautionBrush"] = "#A35A0B",
            ["NotificationCautionTintBrush"] = "#FFF2DE",
            ["NotificationCriticalBrush"] = "#B43B43",
            ["NotificationCriticalTintBrush"] = "#FBE9EA",
            ["SidebarBrush"] = "#F4F5F7",
            ["NavHoverBrush"] = "#EBEEF2",
            ["NavSelectedBrush"] = "#E6EEF8",
            ["ScrollThumbBrush"] = "#A7B2BE",
            ["ScrollThumbHoverBrush"] = "#788796"
        };
        private static readonly Dictionary<string, string> Dark = new()
        {
            ["CanvasBrush"] = "#171A1F",
            ["SurfaceBrush"] = "#20252B",
            ["SubtleBrush"] = "#262C34",
            ["InputBrush"] = "#252B32",
            ["TextBrush"] = "#F2F4F7",
            ["MutedTextBrush"] = "#BDC6D1",
            ["BorderBrush"] = "#39424D",
            ["AccentBrush"] = "#3675BD",
            ["AccentTextBrush"] = "#A6CFFD",
            ["SoftButtonBrush"] = "#2D3641",
            ["SoftButtonTextBrush"] = "#E9EFF7",
            ["WarningLightBrush"] = "#423526",
            ["WarningTextBrush"] = "#F7C783",
            ["SuccessLightBrush"] = "#203C35",
            ["ToastBackgroundBrush"] = "#232930",
            ["ToastBorderBrush"] = "#414B57",
            ["ToastTextBrush"] = "#F1F6FB",
            ["NotificationInfoBrush"] = "#AED7FF",
            ["NotificationInfoTintBrush"] = "#2A3849",
            ["NotificationCautionBrush"] = "#FFD096",
            ["NotificationCautionTintBrush"] = "#423628",
            ["NotificationCriticalBrush"] = "#FFB4B8",
            ["NotificationCriticalTintBrush"] = "#452F34",
            ["SidebarBrush"] = "#1B2026",
            ["NavHoverBrush"] = "#2A333D",
            ["NavSelectedBrush"] = "#263E59",
            ["ScrollThumbBrush"] = "#65707C",
            ["ScrollThumbHoverBrush"] = "#9DAAB7"
        };

        public static void Initialize()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    Preferences? value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsFile));
                    if (value != null)
                    {
                        Language = value.Language == "en" ? "en" : "zh-TW";
                        Theme = value.Theme is "light" or "dark" or "system" ? value.Theme : "system";
                    }
                }
            }
            catch { /* Fall back to built-in preferences. */ }
            Apply();
        }

        public static void SetLanguage(string language)
        {
            Language = language == "en" ? "en" : "zh-TW";
            ApplyLanguage();
            Save();
        }
        public static void SetTheme(string theme)
        {
            Theme = theme is "light" or "dark" or "system" ? theme : "system";
            ApplyTheme();
            Save();
        }
        public static void RefreshSystemTheme()
        {
            if (Theme == "system") ApplyTheme();
        }
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Preferences
                {
                    Language = Language,
                    Theme = Theme
                }));
            }
            catch { /* Do not crash monitoring for UI settings. */ }
        }
        private static void Apply()
        {
            ApplyTheme();
            ApplyLanguage();
        }
        private static void ApplyTheme()
        {
            bool dark = Theme == "dark" || (Theme == "system" && WindowsUsesDarkApps());
            foreach (var pair in dark ? Dark : Light)
                Application.Current.Resources[pair.Key] = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(pair.Value));
            // WPF's native TreeViewItem template uses SystemColors keys, not Foreground alone.
            // Override the system selection brushes at application scope on every theme change.
            var palette = dark ? Dark : Light;
            var selected = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette["NavSelectedBrush"]));
            var selectedText = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette["TextBrush"]));
            var hover = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette["NavHoverBrush"]));
            Application.Current.Resources[SystemColors.HighlightBrushKey] = selected;
            Application.Current.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selected;
            Application.Current.Resources[SystemColors.HighlightTextBrushKey] = selectedText;
            Application.Current.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = selectedText;
            Application.Current.Resources[SystemColors.ControlBrushKey] = hover;
        }
        private static bool WindowsUsesDarkApps()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int setting && setting == 0;
            }
            catch { return false; }
        }
        private static void ApplyLanguage()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "UI", "Locales");
            string path = Path.Combine(root, Language + ".json");
            if (!File.Exists(path)) path = Path.Combine(root, "zh-TW.json");
            if (!File.Exists(path)) return;
            try
            {
                var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(path));
                if (strings == null) return;
                foreach (var pair in strings)
                    Application.Current.Resources[pair.Key] = pair.Value;
            }
            catch { /* Keep existing localized strings on a bad resource. */ }
        }

        public static string Runtime(string zh, string en) => Language == "en" ? en : zh;
    }
}
