using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace USBGuardian.App.Services
{
    // IMPORTANT: Windows PnP nodes are NOT necessarily physical devices.
    internal sealed class DeviceItem
    {
        public string InstanceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Class { get; set; } = "";
        public string Status { get; set; } = "";
        // Only observations and suggested checks, not malware or identity verdicts.
        public bool IsNewObservation { get; set; }
        public bool IsFamiliar { get; set; }
        public bool IsUntrusted { get; set; }

        // These labels describe evidence, not verified physical provenance.
        // A PnP node is not a verified physical device. These are evidence labels.
        public string OriginBucket
        {
            get
            {
                if (IsNewObservation) return "本次新增";
                if (IsSystemComponent || InstanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
                    InstanceId.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase) ||
                    InstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
                    InstanceId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase)) return "系統／驅動";
                if (InstanceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase) ||
                    InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) return "可能外接";
                return "來源未明";
            }
        }
        public int OriginOrder => OriginBucket switch
        {
            "本次新增" => 0, "可能外接" => 1,
            "來源未明" => 2, _ => 3
        };
        public string LocalizedOriginBucket => UiPreferences.Runtime(OriginBucket, OriginBucket switch
        {
            "本次新增" => "Added this session",
            "可能外接" => "Possibly external (USB)",
            "系統／驅動" => "System or driver (inferred)",
            _ => "Origin unknown"
        });

        public string OriginLabel => IsNewObservation
            ? "本次執行後新增（可能外接）"
            : IsSystemComponent ||
              InstanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
              InstanceId.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase) ||
              InstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
              InstanceId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase)
                ? "系統／驅動項目（推定）"
                : "啟動時已存在（來源待確認）";

        public string OriginExplanation => IsNewObservation
            ? "本次程式執行後才在 Windows 清單出現；可能是新插入、驅動重載或重新配對，無法直接認證物理來源。"
            : IsSystemComponent
                ? "名稱及類別符合常見系統節點，但內建或外接的硬體都可能使用此類節點。"
                : "啟動時已存在，可能是內建設備或早已接好的外接設備。";

        public bool HadFunctionChange { get; set; }
        public bool IsSystemComponent => Category == "USB 控制器與集線器" ||
            (Category == "其他輸入裝置（HID）" &&
             (Name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
              Name.StartsWith("USB Input Device", StringComparison.OrdinalIgnoreCase)));
        public int AttentionLevel => IsUntrusted || HadFunctionChange ||
            (IsNewObservation && (Category == "鍵盤與滑鼠" || Category == "網路介面")) ? 2 :
            (IsNewObservation && !IsSystemComponent ? 1 : 0);
        public string AttentionLabel => IsUntrusted ? "你已標記為不信任" :
            AttentionLevel == 2 ? "新增功能，請確認" :
            AttentionLevel == 1 ? "新觀察到" :
            IsFamiliar ? "有熟悉紀錄" :
            IsSystemComponent ? "系統技術項目" : "用途資訊";
        public string DisplayGroup => IsUntrusted ? "0 · 你標記為不信任的項目" :
            AttentionLevel == 2 ? "1 · 新功能需確認" :
            AttentionLevel == 1 ? "2 · 新觀察到的裝置" :
            IsFamiliar ? "3 · 已記錄的裝置" :
            IsSystemComponent ? "5 · 系統技術項目" : "4 · 一般用途裝置";
        public string SuggestedAction
        {
            get
            {
                if (IsUntrusted) return "你已在此電腦記錄為不信任；目前僅是 App 標記，未代表 Windows 已封鎖。可辨識的 USB 儲存介面可手動要求停用。";
                if (HadFunctionChange) return "記錄中的裝置用途與目前不同。請確認是否更換了裝置或驅動程式。";
                if (IsNewObservation && Category == "鍵盤與滑鼠")
                    return "Windows 新增了輸入裝置項目；如果你沒有連接鍵盤或滑鼠，請確認剛接入的設備。";
                if (IsNewObservation && Category == "網路介面")
                    return "Windows 新增網路介面；如果你沒有接入網路設備，請確認來源及網路設定。";
                if (IsNewObservation) return "本次執行後才觀察到此項目。請確認是否與剛接入的設備一致。";
                if (IsSystemComponent) return "通常是系統或裝置的驅動節點；不建議只因名稱陌生就移除或停用。";
                if (Category == "儲存與讀卡裝置") return "陌生隨身碟請先檢查檔案，再視需要執行 Defender 掃描。";
                if (Category == "相機與影像" || Category == "音訊與耳機")
                    return "這類裝置可能具有收音或影像功能；若不是你預期連接的設備，請確認。";
                return "Windows 只提供用途和運作狀態；沒有足夠證據能證明此裝置安全。";
            }
        }
        // Categories are UI groupings of Windows PnP nodes, not proof of physical identity.
        public string Category
        {
            get
            {
                if (Class.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase) ||
                    Class.Equals("MEDIA", StringComparison.OrdinalIgnoreCase)) return "音訊與耳機";
                if (Class.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                    InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return "藍牙裝置";
                if (Class.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
                    Class.Equals("Image", StringComparison.OrdinalIgnoreCase)) return "相機與影像";
                if (Class.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                    Class.Equals("Mouse", StringComparison.OrdinalIgnoreCase)) return "鍵盤與滑鼠";
                if (Class.Equals("DiskDrive", StringComparison.OrdinalIgnoreCase) ||
                    Class.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase) ||
                    InstanceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)) return "儲存與讀卡裝置";
                if (Class.Equals("Net", StringComparison.OrdinalIgnoreCase)) return "網路介面";
                if (Class.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                    Class.Equals("USBDevice", StringComparison.OrdinalIgnoreCase) ||
                    Class.Equals("USBController", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("hub", StringComparison.OrdinalIgnoreCase)) return "USB 控制器與集線器";
                if (Class.Equals("HIDClass", StringComparison.OrdinalIgnoreCase)) return "其他輸入裝置（HID）";
                return "其他裝置";
            }
        }
        public string Purpose => Category switch
        {
            "音訊與耳機" => "音訊播放、錄音或耳機相關項目。部分耳機可能同時出現多筆。",
            "藍牙裝置" => "藍牙通訊相關項目；不一定是剛接入的 USB。",
            "相機與影像" => "攝影機或影像擷取設備相關項目。",
            "鍵盤與滑鼠" => "可向電腦提供按鍵或指標輸入；未知來源請先確認。",
            "儲存與讀卡裝置" => "提供檔案儲存或讀取記憶卡的裝置。",
            "網路介面" => "可以建立或提供網路連線的介面。",
            "USB 控制器與集線器" => "通常是 USB 埠、集線器或系統內部的控制節點。",
            "其他輸入裝置（HID）" => "輸入/控制類別，可能是觸控、熱鍵、控制按鈕等。",
            _ => "Windows 尚無足夠資訊判定具體用途。"
        };
        // User-facing translation does not change the stable category keys used in filters.
        public string LocalizedCategory => Category switch
        {
            "音訊與耳機" => UiPreferences.Runtime(Category, "Audio & headphones"),
            "藍牙裝置" => UiPreferences.Runtime(Category, "Bluetooth"),
            "相機與影像" => UiPreferences.Runtime(Category, "Cameras"),
            "鍵盤與滑鼠" => UiPreferences.Runtime(Category, "Keyboard & mouse"),
            "儲存與讀卡裝置" => UiPreferences.Runtime(Category, "Storage & card readers"),
            "網路介面" => UiPreferences.Runtime(Category, "Network adapter"),
            "USB 控制器與集線器" => UiPreferences.Runtime(Category, "USB hubs & controllers"),
            "其他輸入裝置（HID）" => UiPreferences.Runtime(Category, "Other input (HID)"),
            _ => UiPreferences.Runtime(Category, "Other devices")
        };
        public string LocalizedOrigin => UiPreferences.Runtime(OriginLabel,
            IsNewObservation ? "New since app started (not verified as external)" :
            IsSystemComponent ? "System/driver entry (inferred)" :
            "Present when app started (origin unknown)");
        public string LocalizedAttention => UiPreferences.Runtime(AttentionLabel,
            IsUntrusted ? "You marked this untrusted" :
            AttentionLevel == 2 ? "Needs review" :
            AttentionLevel == 1 ? "Recently observed" :
            IsFamiliar ? "Remembered" :
            IsSystemComponent ? "System component" : "Device info");
        public string LocalizedGroup => UiPreferences.Runtime(
            IsUntrusted ? "標記不信任" :
            AttentionLevel == 2 ? "需要確認" :
            AttentionLevel == 1 ? "最近新增" :
            IsFamiliar ? "已記錄" :
            IsSystemComponent ? "系統項目" : "一般裝置",
            IsUntrusted ? "Marked untrusted" :
            AttentionLevel == 2 ? "Needs review" :
            AttentionLevel == 1 ? "Recently added" :
            IsFamiliar ? "Saved devices" :
            IsSystemComponent ? "System entries" : "Other devices");
        public string LocalizedPurpose => UiPreferences.Runtime(Purpose, Category switch
        {
            "音訊與耳機" => "Audio output/input. One headset can have multiple entries.",
            "藍牙裝置" => "Bluetooth connection; not necessarily a newly inserted USB.",
            "相機與影像" => "Camera or image capture device.",
            "鍵盤與滑鼠" => "Can send keyboard or pointer input to the PC.",
            "儲存與讀卡裝置" => "Can read files or memory cards.",
            "網路介面" => "Can provide network connectivity.",
            "USB 控制器與集線器" => "USB hub or system controller entry.",
            "其他輸入裝置（HID）" => "Input or control functions such as hotkeys.",
            _ => "Windows has not provided enough information to identify its purpose."
        });
        public string LocalizedAction => UiPreferences.Runtime(SuggestedAction,
            IsUntrusted ? "You marked this entry untrusted. Only eligible USB storage interfaces can be manually disabled." :
            HadFunctionChange ? "The recorded function changed. Verify device and driver updates." :
            IsNewObservation && Category == "鍵盤與滑鼠" ? "Unexpected keyboard or mouse? Check what you just connected." :
            IsNewObservation && Category == "網路介面" ? "Unexpected network adapter? Check its source and network settings." :
            IsNewObservation ? "This entry appeared after USB Guardian started. Verify the source." :
            IsSystemComponent ? "Likely a system or driver entry. Do not disable it just because the name is unfamiliar." :
            Category == "儲存與讀卡裝置" ? "For an unknown drive, inspect files and consider Defender scanning." :
            "The device class and status cannot establish that a device is safe.");
        public string ShortStatus => Status.Equals("OK", StringComparison.OrdinalIgnoreCase)
            ? "正常運作" : (string.IsNullOrWhiteSpace(Status) ? "狀態未知" : Status);
        public override string ToString() => $"{Category}   ·   {Name}";
    }

    internal static class DeviceProbe
    {
        public static async Task<List<DeviceItem>> QueryAsync(CancellationToken token)
        {
            // A single Get-PnpDevice call, no repeated Get-PnpDeviceProperty queries.
            // This avoids the timeouts seen in earlier prototypes.
            string script = @"
$ErrorActionPreference='Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$items=@(Get-PnpDevice -PresentOnly | Where-Object {
 $_.InstanceId -match '^(USB|USBSTOR|HID|BTH|SWD\\MMDEVAPI)\\' -or
 $_.Class -in @('AudioEndpoint','MEDIA','Bluetooth','Camera','Image','Net','Keyboard','Mouse','DiskDrive','SCSIAdapter','USB','USBDevice')
} | Select-Object InstanceId, FriendlyName, Class, Status)
ConvertTo-Json -InputObject $items -Compress -Depth 3
";
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            ProcessStartInfo start = new()
            {
                FileName = exe,
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using Process process = Process.Start(start) ?? throw new IOException("無法啟動裝置查詢程序");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(12));
            try { await process.WaitForExitAsync(budget.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("裝置查詢逾時；可稍後按「重新查詢」重試。");
            }
            string output = await stdout;
            string error = await stderr;
            if (process.ExitCode != 0) throw new IOException(error.Length > 250 ? error[..250] : error);
            if (string.IsNullOrWhiteSpace(output)) return new List<DeviceItem>();
            using JsonDocument doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("PnP 查詢資料格式不正確");
            List<DeviceItem> list = new();
            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                static string Read(JsonElement e, string key) =>
                    e.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
                string id = Read(element, "InstanceId");
                if (id.Length == 0) continue;
                list.Add(new DeviceItem
                {
                    InstanceId = id,
                    Name = Read(element, "FriendlyName") is string n && n.Length > 0 ? n : "未命名裝置",
                    Class = Read(element, "Class"),
                    Status = Read(element, "Status")
                });
            }
            return list.GroupBy(x => x.InstanceId, StringComparer.OrdinalIgnoreCase)
                       .Select(x => x.First()).ToList();
        }
    }
}
