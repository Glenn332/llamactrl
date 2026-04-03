param([string]$RID = "win-x64")

$Root = $PSScriptRoot

Write-Host "==> Building LlamaCtrl for $RID" -ForegroundColor Cyan
Write-Host ""

# Step 1: Frontend
Write-Host "[1/2] Building React frontend..." -ForegroundColor Yellow
Set-Location "$Root\frontend"
npm install --silent
npm run build
Write-Host "      Frontend built -> src\LlamaCtrl\wwwroot\" -ForegroundColor Green
Write-Host ""

# Step 2: Backend
Write-Host "[2/2] Building .NET backend..." -ForegroundColor Yellow
Set-Location "$Root\src\LlamaCtrl"
dotnet publish -c Release -r $RID --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o "$Root\dist\$RID"

Write-Host ""
Write-Host "==> Build complete!" -ForegroundColor Green
Write-Host "    Binary: $Root\dist\$RID\llamactrl.exe" -ForegroundColor Green
