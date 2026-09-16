# Build and package RemoteLAN installer using Inno Setup
$ErrorActionPreference = "Stop"

Write-Host "Building solution in Release configuration..." -ForegroundColor Cyan
dotnet build RemoteLAN.slnx -c Release

Write-Host "Publishing self-contained RemoteLAN win-x64 binaries (with bundled .NET 8 runtime)..." -ForegroundColor Cyan
dotnet publish src/RemoteLAN/RemoteLAN.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o bin/publish/

$isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    $isccPath = "C:\Program Files\Inno Setup 6\ISCC.exe"
}

if (-not (Test-Path $isccPath)) {
    throw "Inno Setup compiler (ISCC.exe) not found!"
}

Write-Host "Compiling Inno Setup installer using $isccPath..." -ForegroundColor Cyan
& $isccPath "installer\RemoteLAN_Setup.iss"

Write-Host "Installer created successfully in dist/!" -ForegroundColor Green
