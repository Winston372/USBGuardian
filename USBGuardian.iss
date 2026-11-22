; Optional Inno Setup script — requires Inno Setup 6 on a Windows development PC.
; Run publish-win-x64.ps1 first. No changes to user registry without a selected task.
#define AppVersion "3.2.0-rc1"

[Setup]
AppId={{4D163A3E-D639-4711-AFB6-CF28C7D7E961}
AppName=USB Guardian
AppVersion={#AppVersion}
AppPublisher=USB Guardian Open Source
DefaultDirName={localappdata}\Programs\USBGuardian
DefaultGroupName=USB Guardian
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=USBGuardian-Setup-{#AppVersion}-win-x64
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\USBGuardian.App.exe
ArchitecturesAllowed=x64compatible

[Files]
Source: "dist\USBGuardian-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\USB Guardian"; Filename: "{app}\USBGuardian.App.exe"

[Tasks]
Name: "autostart"; Description: "登入 Windows 後自動啟動（選用）"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "USBGuardian"; ValueData: """{app}\USBGuardian.App.exe"" --background"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\USBGuardian.App.exe"; Description: "啟動 USB Guardian"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Preserve user-local records and encrypted quarantine after uninstall.
; Never remove quarantine automatically: doing so could cause irreversible data loss.
; Users should restore/quarantine-delete items before deleting LocalAppData records.
