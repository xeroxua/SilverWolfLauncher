# Silver Wolf Launcher - Release Build Script 🚀
# This script packages the launcher into a Single-File executable for distribution.

Write-Host "📦 Starting Single-File Release Build..." -ForegroundColor Cyan

# Define Output Directory
$outDir = "./Publish"

# Clean previous build
if (Test-Path $outDir) {
    Remove-Item -Path $outDir -Recurse -Force
}

# Run Dotnet Publish
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $outDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Build Successful! Check the '$outDir' folder." -ForegroundColor Green
    Write-Host "🚀 File: SilverWolfLauncher.exe" -ForegroundColor Yellow
} else {
    Write-Host "`n❌ Build Failed. Please check errors above." -ForegroundColor Red
}

pause
