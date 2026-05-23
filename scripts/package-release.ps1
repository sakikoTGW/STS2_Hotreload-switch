# Build Release and zip for GitHub Releases.
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

if (-not $Version) {
    $json = Get-Content (Join-Path $root "ModHotReload.json") -Raw | ConvertFrom-Json
    $Version = $json.version
}

$deploy = Get-ModHotReloadDeployPath
if (-not $deploy) {
    $env:STS2_PATH = Require-Sts2Path
    Write-Host "=== dotnet build Release ===" -ForegroundColor Cyan
    Push-Location $root
    try { dotnet build ModHotReload.csproj -c Release | Out-Host }
    finally { Pop-Location }
    $deploy = Get-ModHotReloadDeployPath
}
if (-not $deploy) { throw "Deploy path not found after build." }

$dist = Join-Path $root "dist"
$stage = Join-Path $dist "ModHotReload"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$include = @(
    "ModHotReload.dll",
    "ModHotReload.Core.dll",
    "ModHotReload.StartupHook.dll",
    "ModHotReload.pck",
    "ModHotReload.json",
    "Install.bat",
    "sts2-launch.cmd"
)
foreach ($f in $include) {
    Copy-Item (Join-Path $deploy $f) $stage -Force
}

$installTxt = @"
# Mod Hot Reload v$Version

1. Copy this ModHotReload folder to: <STS2>/mods/ModHotReload/
2. Run Install.bat once, then launch from Steam.
3. Enable Mod Hot Reload in the in-game mod list.
"@
Set-Content (Join-Path $stage "INSTALL.txt") -Value $installTxt -Encoding UTF8

$zip = Join-Path $dist "ModHotReload-v$Version.zip"
Compress-Archive -Path $stage -DestinationPath $zip -Force
Write-Host "OK: $zip ($((Get-Item $zip).Length) bytes)" -ForegroundColor Green
