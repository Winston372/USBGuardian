# Build a portable, self-contained Windows x64 folder for USB Guardian.
# Run from an ordinary (non-elevated) Windows PowerShell with the .NET 10 SDK.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$destination = Join-Path $PSScriptRoot "dist\USBGuardian-win-x64"
if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
New-Item -Path $destination -ItemType Directory -Force | Out-Null
Write-Host "Publishing USB Guardian (Windows x64)..."
dotnet restore .\USBGuardian.sln -r win-x64
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
dotnet publish .\USBGuardian.App\USBGuardian.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o $destination -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }
$analyzer = Join-Path $destination "SafeLab"
New-Item -Path $analyzer -ItemType Directory -Force | Out-Null
dotnet publish .\USBGuardian.Analyzer\USBGuardian.Analyzer.csproj -c Release -r win-x64 --self-contained true --no-restore -o $analyzer -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "Analyzer publish failed" }
if (!(Test-Path (Join-Path $destination "FirstUseNotice.zh-TW.txt"))) { throw "Missing Chinese notice file; aborting release" }
if (!(Test-Path (Join-Path $destination "FirstUseNotice.en.txt"))) { throw "Missing English notice file; aborting release" }
if (!(Test-Path (Join-Path $destination "UI\Locales\en.json"))) { throw "Missing English UI resources; aborting release" }
if (!(Test-Path (Join-Path $destination "UI\Locales\zh-TW.json"))) { throw "Missing Chinese UI resources; aborting release" }
if (!(Test-Path (Join-Path $analyzer "USBGuardian.Analyzer.exe"))) { throw "Missing analyzer executable; aborting release" }
Write-Host "Release files ready: $destination"
Write-Host "Before sharing: verify on Windows Home, measure resources and consider code signing."
