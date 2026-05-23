# Prints Steam launch options for DOTNET_STARTUP_HOOKS (fallback if runtimeconfig hook missing).
param([string]$Sts2Path = $env:STS2_PATH)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

if ([string]::IsNullOrWhiteSpace($Sts2Path)) {
    $Sts2Path = Get-Sts2InstallPath
}
if (-not $Sts2Path) {
    Write-Error "STS2 path not found. Set env STS2_PATH or install the game via Steam."
}

$hookDll = Join-Path $Sts2Path "mods\ModHotReload\ModHotReload.StartupHook.dll"
if (-not (Test-Path -LiteralPath $hookDll)) {
    Write-Error "Missing StartupHook DLL: $hookDll`nBuild first: dotnet build ModHotReload.csproj -c Release"
}

$launchOpts = "DOTNET_STARTUP_HOOKS=$hookDll"
$bootLog = Join-Path $env:LOCALAPPDATA "STS2_ModHotReload\startup-hook.log"

Write-Host ""
Write-Host "=== Steam launch options (game Properties -> Launch Options) ===" -ForegroundColor Cyan
Write-Host $launchOpts -ForegroundColor Green
Write-Host ""
Write-Host "=== Or in this PowerShell session before starting the game ===" -ForegroundColor Cyan
Write-Host ('$env:DOTNET_STARTUP_HOOKS="' + $hookDll + '"')
Write-Host ""
Write-Host ("Log file: " + $bootLog)
Write-Host ""
Write-Host "Prefer: scripts\patch-runtimeconfig.ps1 (no Steam launch options needed)" -ForegroundColor Yellow
Write-Host ""
