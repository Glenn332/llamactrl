param([string]$RID = "win-x64")

$Root = $PSScriptRoot
$Dist = "$Root\dist\$RID"

# ── Build ──────────────────────────────────────────────────────────────────

Write-Host "==> Building LlamaCtrl for $RID" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/2] Building React frontend..." -ForegroundColor Yellow
Set-Location "$Root\src\frontend"
npm install --silent
npm run build
Write-Host "      Done." -ForegroundColor Green
Write-Host ""

Write-Host "[2/2] Publishing .NET backend..." -ForegroundColor Yellow
Set-Location "$Root\src\LlamaCtrl"
dotnet publish -c Release -r $RID --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $Dist
Write-Host "      Done." -ForegroundColor Green
Write-Host ""

# ── Install ────────────────────────────────────────────────────────────────

$InstallDir = "$env:LOCALAPPDATA\llamactrl"

Write-Host "==> Installing llamactrl" -ForegroundColor Cyan
Write-Host "    Assets  -> $InstallDir" -ForegroundColor Gray
Write-Host "    Command -> llamactrl (user PATH)" -ForegroundColor Gray
Write-Host ""

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
Copy-Item $Dist $InstallDir -Recurse

$currentPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($currentPath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$InstallDir;$currentPath", "User")
    Write-Host "Added $InstallDir to user PATH." -ForegroundColor Green
    Write-Host "Restart your terminal for 'llamactrl' to be available." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done! Run: llamactrl" -ForegroundColor Green
