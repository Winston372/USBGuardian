# USB Guardian

**USB 裝置安全檢測與風險辨識工具**  
**USB Security Awareness & Device Inspection for Windows**

USB Guardian 是一款使用 **C#、WPF 與 .NET** 開發的 Windows 桌面應用程式，旨在協助一般使用者辨識外接 USB 裝置的潛在安全風險，並提供直覺、清楚且容易理解的裝置與檔案檢查介面。

USB Guardian is a Windows desktop application developed with **C#, WPF, and .NET**, designed to help users inspect external USB storage devices, recognize potential security risks, and develop safer habits when interacting with removable media.

本專案以「**讓 USB 安全知識更容易被理解與實踐**」為核心理念，將裝置辨識、檔案檢視、安全提示與系統掃描功能整合於單一介面。

The project focuses on making USB security awareness more accessible by combining device inspection, file browsing, security notifications, and system-integrated scanning in a single application.

---

## 專案理念 | Project Motivation

USB 隨身碟與其他外接裝置廣泛應用於學校、辦公室與日常生活。然而，使用者往往無法直接判斷未知裝置是否安全，也容易忽略可疑檔案、副檔名或裝置異常行為。

USB Guardian 希望透過容易操作的圖形化介面，讓使用者在開啟未知檔案之前，先了解裝置及其內容，建立更謹慎的資訊安全習慣。

USB storage devices are widely used in education, workplaces, and everyday computing. However, users may not always recognize suspicious files, unfamiliar devices, or potentially unsafe behavior.

USB Guardian encourages users to inspect removable media before interacting with unknown content, promoting safer and more informed decisions.

---

## 主要功能 | Key Features

### 1. USB 裝置偵測與辨識 | USB Device Detection

- 偵測外接 USB 儲存裝置
- 顯示可用磁碟及相關裝置資訊
- 協助使用者識別目前連接的外接儲存媒體
- 提供裝置活動及安全提示

**English**

- Discover external USB storage devices
- Display available drives and device information
- Help users identify connected removable media
- Provide device activity and security notifications

### 2. 檔案與資料夾檢視 | File and Folder Inspection

- 以樹狀結構瀏覽 USB 裝置中的檔案與資料夾
- 顯示檔案名稱、路徑及基本屬性
- 檢視檔案技術資訊
- 協助辨識可疑副檔名與檔案特徵

**English**

- Browse files and folders through a hierarchical tree view
- Inspect file names, paths, and attributes
- View technical file information
- Identify potentially suspicious file extensions and characteristics

### 3. 檔案技術資訊 | File Metadata

提供檔案資訊檢視功能，協助使用者進一步了解檔案內容與屬性，包括適用情況下的：

- 檔案大小與類型
- 檔案路徑與屬性
- SHA-256 雜湊值
- 十六進位（HEX）資訊

**English**

File inspection provides technical information where applicable, including file size, type, attributes, SHA-256 hashes, and hexadecimal data.

### 4. Microsoft Defender 整合 | Defender Integration

USB Guardian 提供 Microsoft Defender 掃描入口，讓使用者能透過 Windows 內建安全工具進一步檢查指定裝置。

USB Guardian integrates with Microsoft Defender scanning capabilities, allowing users to perform additional security checks using Windows security tools.

**注意：** 掃描結果不能保證裝置或檔案完全安全。

### 5. 安全提示與風險意識 | Security Awareness

透過裝置狀態、風險提示及檔案資訊，協助使用者養成以下習慣：

- 不隨意插入來源不明的 USB 裝置
- 不直接執行未知檔案
- 注意可疑副檔名與異常檔案
- 在開啟內容前先進行必要檢查

**English**

The application promotes responsible USB usage, including avoiding unknown devices, inspecting unfamiliar files, recognizing suspicious extensions, and checking removable media before opening content.

### 6. 深色與淺色模式 | Light and Dark Modes

提供深色與淺色介面，改善不同環境下的操作體驗與資訊可讀性。

Supports light and dark appearance modes for different user preferences and viewing environments.

### 7. 問題回報 | Issue Reporting

提供問題回報入口，方便使用者透過電子郵件用戶端提供使用回饋及錯誤資訊。

Includes an optional issue-reporting workflow through the user's email client.

---

## 系統需求 | System Requirements

| 項目 / Item | 需求 / Requirement |
|---|---|
| 作業系統 / OS | Windows 10 / Windows 11 |
| 開發語言 / Language | C# |
| UI 框架 / Framework | WPF |
| 開發平台 / Platform | .NET 8 |
| 開發工具 / IDE | Visual Studio（建議 / Recommended） |
| 編譯工具 / SDK | .NET 10 SDK（依目前專案 README 的編譯要求） |
| 安全掃描 / Security | Microsoft Defender（整合功能） |

本專案使用 Windows 專屬的 WPF 技術，因此不支援直接在 macOS 或 Linux 上執行圖形介面。

This project uses Windows Presentation Foundation (WPF) and is intended for Windows desktop environments.

---

## 編譯與執行 | Build and Run

### 1. 下載原始碼 | Download Source Code

在 GitHub Repository 選擇 **Code → Download ZIP**，或使用 Git：

```bash
git clone https://github.com/Winston372/USBGuardian.git
cd USBGuardian
```

### 2. 還原與編譯 | Restore and Build

在 Windows PowerShell 或支援 .NET SDK 的終端機中執行：

```powershell
dotnet restore .\USBGuardian.sln
dotnet build .\USBGuardian.sln -c Release
```

### 3. 執行程式 | Run Application

```powershell
dotnet run --project .\USBGuardian.App\USBGuardian.App.csproj
```

### 4. 發布 Windows 版本 | Publish for Windows

```powershell
.\publish-win-x64.ps1
```

發布結果及可執行檔位置依腳本設定而定。

The publish script is intended to prepare a Windows x64 build. Output locations depend on the script configuration.

---

## 使用方式 | How to Use

1. 啟動 USB Guardian。
2. 連接需要檢查的 USB 儲存裝置。
3. 在介面中選擇對應磁碟。
4. 瀏覽檔案及資料夾結構。
5. 檢視可疑檔案的屬性與技術資訊。
6. 視需要使用 Microsoft Defender 掃描。
7. 根據檢查結果決定是否進一步操作檔案。

**English**

1. Launch USB Guardian.
2. Connect a USB storage device.
3. Select the appropriate drive.
4. Browse its files and folders.
5. Inspect unfamiliar files and metadata.
6. Run a Microsoft Defender scan when appropriate.
7. Make an informed decision before interacting with the content.

---

## 專案架構 | Project Structure

```text
USBGuardian/
├── USBGuardian.App/          # WPF 桌面應用程式
├── USBGuardian.Analyzer/     # 隔離式檢查輔助程式
├── .github/workflows/        # GitHub Actions
├── USBGuardian.sln           # Visual Studio Solution
├── USBGuardian.iss           # Windows 安裝程式設定
├── publish-win-x64.ps1       # Windows 發布腳本
├── README.md                 # 專案說明
├── SECURITY.md               # 安全政策
├── LICENSE                   # 授權條款
└── .gitignore                # Git 忽略規則
```

---

## 安全聲明 | Security Disclaimer

USB Guardian 是一款以安全意識教育及裝置檢查為目的的輔助工具，**並非完整的防毒軟體，也不能保證偵測所有惡意 USB 裝置或檔案**。

使用者仍應遵守基本資訊安全原則，包括避免使用未知來源的裝置、不執行不可信任的程式，以及定期更新 Windows 與安全防護工具。

USB Guardian is a security awareness and device inspection utility. It is **not a replacement for antivirus software, endpoint protection, or professional malware analysis tools**.

A successful scan or the absence of warnings does not guarantee that a device or file is safe.

Test potentially destructive actions only on disposable or backed-up data.

---

## 開發方向 | Future Development

本專案未來希望持續改善：

- USB 裝置辨識與風險分類
- 檔案檢查與安全提示
- 介面可讀性及無障礙操作
- 多語言使用者體驗
- 裝置事件與安全紀錄
- 程式穩定性與錯誤處理

**English**

Future development priorities include improved USB device classification, file inspection, accessibility, multilingual support, device event records, and application reliability.

以上為開發方向，不代表所有功能已完成或正式發布。

These are development goals and do not imply that all features are currently implemented.

---

## 開源授權 | License

本專案採用 **MIT License**。

This project is distributed under the MIT License. See the `LICENSE` file for details.

---

## 安全問題回報 | Security Reporting

若發現安全漏洞，請參考 `SECURITY.md` 中的回報方式。

For security vulnerabilities, please follow the reporting guidance in `SECURITY.md`.

---

**USB Guardian — Inspect Before You Trust.**

**先辨識，再信任。讓每一次 USB 連接都多一分安全意識。**
