# ── LlamaCtrl Installer for Windows ────────────────────────────────────────
# Downloads the latest release binary from GitHub and installs it.
# Usage: irm <raw-url>/install.ps1 | iex

$ErrorActionPreference = "Stop"

# GitHub repository — update this if you fork the project
$Repo = "Glenn332/llamactrl"

# Try to auto-detect from git remote if running from a clone
try {
    $null = git rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -eq 0) {
        $remoteUrl = git remote get-url origin 2>$null
        if ($remoteUrl -match 'github\.com[:/]([^/]+/[^/.]+?)(?:\.git)?$') {
            $Repo = $Matches[1]
        }
    }
} catch {
    # git not available, use default
}

Write-Host "==> LlamaCtrl Installer" -ForegroundColor Cyan
Write-Host "    Repository: $Repo"
Write-Host ""

# ── Detect platform ───────────────────────────────────────────────────────

$Arch = $env:PROCESSOR_ARCHITECTURE
switch ($Arch) {
    "ARM64" { $RID = "win-arm64" }
    "AMD64" { $RID = "win-x64" }
    default {
        Write-Host "Error: Unsupported architecture: $Arch" -ForegroundColor Red
        exit 1
    }
}

$Asset = "llamactrl-${RID}.zip"
Write-Host "[1/4] Detected platform: $RID"

# ── Fetch latest release URL ──────────────────────────────────────────────

$ApiUrl = "https://api.github.com/repos/$Repo/releases/latest"

Write-Host "[2/4] Fetching latest release info..."

try {
    $release = Invoke-RestMethod -Uri $ApiUrl -Headers @{ "User-Agent" = "llamactrl-installer" }
} catch {
    Write-Host "Error: Could not fetch release info from $ApiUrl" -ForegroundColor Red
    Write-Host "       $_" -ForegroundColor Red
    exit 1
}

$assetInfo = $release.assets | Where-Object { $_.name -eq $Asset }

if (-not $assetInfo) {
    Write-Host "Error: Could not find asset '$Asset' in the latest release." -ForegroundColor Red
    Write-Host "       Available assets:" -ForegroundColor Yellow
    $release.assets | ForEach-Object { Write-Host "         $($_.name)" }
    exit 1
}

$DownloadUrl = $assetInfo.browser_download_url
$Tag = $release.tag_name

Write-Host "       Latest version: $Tag"
Write-Host "       Asset: $Asset"

# ── Download ──────────────────────────────────────────────────────────────

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "llamactrl-install-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

$ArchivePath = Join-Path $TempDir $Asset

Write-Host "[3/4] Downloading $Asset..."

try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $ArchivePath -UseBasicParsing
} catch {
    Write-Host "Error: Download failed: $_" -ForegroundColor Red
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    exit 1
}

# ── Extract and install ───────────────────────────────────────────────────

Write-Host "[4/4] Installing..."

try {
    Expand-Archive -Path $ArchivePath -DestinationPath $TempDir -Force
} catch {
    Write-Host "Error: Failed to extract archive: $_" -ForegroundColor Red
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    exit 1
}

# Find the executable
$Binary = Get-ChildItem -Path $TempDir -Filter "llamactrl.exe" -Recurse | Select-Object -First 1

if (-not $Binary) {
    Write-Host "Error: Could not find 'llamactrl.exe' in the archive." -ForegroundColor Red
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
    exit 1
}

# Determine install location
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\llamactrl"

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Copy-Item -Path $Binary.FullName -Destination (Join-Path $InstallDir "llamactrl.exe") -Force

# Clean up
Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "==> llamactrl.exe installed to $InstallDir" -ForegroundColor Green

# Check if install dir is on PATH
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$InstallDir*") {
    Write-Host ""
    Write-Host "NOTE: $InstallDir is not on your PATH." -ForegroundColor Yellow
    Write-Host "      To add it, run:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  [Environment]::SetEnvironmentVariable('PATH', `"$InstallDir;`$env:PATH`", 'User')" -ForegroundColor White
    Write-Host ""
    Write-Host "      Then restart your terminal."
}

Write-Host ""
Write-Host "Done! Run: llamactrl" -ForegroundColor Green
