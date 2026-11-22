# USB Guardian

USB Guardian is a Windows desktop application for inspecting external USB storage devices and improving device-safety awareness. Built with C# and WPF.

## Requirements

- Windows 10/11
- .NET 10 SDK
- Visual Studio with .NET desktop development workload (optional)

## Build and run

```powershell
dotnet restore .\USBGuardian.sln
dotnet build .\USBGuardian.sln -c Release
dotnet run --project .\USBGuardian.App\USBGuardian.App.csproj
```

To publish, run `./publish-win-x64.ps1` in PowerShell.

## Features

- USB device discovery and file browsing
- File metadata and inspection
- Microsoft Defender scan integration
- Device activity and security notifications
- Light and dark appearance modes
- Optional issue reporting through the user's email client

## Safety

Treat removable media as untrusted. Inspect suspicious files without executing them. USB Guardian does not guarantee that a device or file is safe. Test destructive actions only on disposable data.

## Project structure

- `USBGuardian.App/` — WPF desktop application
- `USBGuardian.Analyzer/` — isolated inspection helper
- `.github/workflows/` — Windows build workflow

## Status

Source-only release candidate based on v3.5.9. XML files were checked for well-formedness; Windows compilation, device testing and visual acceptance remain outstanding.

## License

See [LICENSE](LICENSE).
