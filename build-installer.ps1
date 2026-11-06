# Publish app and build Inno Setup installer (requires Inno Setup 6).
# Run from repo:  StdfAnalyzer\build-installer.ps1

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

# 安装包若内嵌「单文件自包含」超大 exe，Inno 解压时易触发 CRC 报错（The setup files are corrupted）。
# 安装程序改为打包多文件自包含目录，体积相近但兼容性更好。便携单文件 exe 请自行: PublishSingleFile=true
Write-Host "==> dotnet publish (win-x64, self-contained, multi-file for installer)..." -ForegroundColor Cyan
$pub = Join-Path $here "bin\Publish"
if (Test-Path $pub) {
  Remove-Item (Join-Path $pub "*") -Recurse -Force -ErrorAction SilentlyContinue
}
dotnet publish StdfAnalyzer.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:DebugType=none -o "bin\Publish"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem $pub -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$candidates = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  Write-Host ""
  Write-Host "Inno Setup 6 (ISCC.exe) not found." -ForegroundColor Yellow
  Write-Host "Install from: https://jrsoftware.org/isdl.php  then run this script again." -ForegroundColor Yellow
  Write-Host "Published EXE is ready: $here\bin\Publish\StdfAnalyzer.exe" -ForegroundColor Green
  exit 1
}

Write-Host "==> Building installer: $iscc" -ForegroundColor Cyan
& $iscc "$here\installer\StdfAnalyzer.iss"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$out = Join-Path $here "bin\Installer"
Write-Host ""
Write-Host "Installer output: $out" -ForegroundColor Green
Get-ChildItem $out -Filter "*.exe" | ForEach-Object { Write-Host "  $($_.FullName)" }
