# Builds the installer locally: publish the app, fetch the driver installers,
# compile the Inno Setup script. Requires Inno Setup 6 (winget install JRSoftware.InnoSetup).
param(
  [string]$Version = "1.1.0",
  [string]$ViGEmUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe",
  [string]$HidHideUrl = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$deps = Join-Path $PSScriptRoot 'deps'
New-Item -ItemType Directory -Force $deps | Out-Null

Write-Host "== publish v$Version"
dotnet publish "$root\src\WolverineRemapper\WolverineRemapper.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $dist
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "== driver installers"
if (-not (Test-Path "$deps\ViGEmBus_Setup.exe")) { Invoke-WebRequest $ViGEmUrl -OutFile "$deps\ViGEmBus_Setup.exe" }
if (-not (Test-Path "$deps\HidHide_Setup.exe"))  { Invoke-WebRequest $HidHideUrl -OutFile "$deps\HidHide_Setup.exe" }

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($cmd) { $iscc = $cmd.Source } }
if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup" }

Write-Host "== compile installer"
& $iscc "/DAppVersion=$Version" "/DSourceDir=$dist" "/DDepsDir=$deps" "$PSScriptRoot\WolverineRemapper.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Get-ChildItem "$PSScriptRoot\output" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
