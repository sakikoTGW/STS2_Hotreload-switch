# While game is running: push built DLL to ModHotReload staging and touch manifest.
# Usage:
#   .\scripts\push-mod-staging.ps1 -ModId MyMod -DllPath "path\to\MyMod.dll"
#   .\scripts\push-mod-staging.ps1 -ModId MyMod

param(
    [Parameter(Mandatory = $true)]
    [string]$ModId,
    [string]$DllPath = "",
    [switch]$NoTouchJson
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

$staging = Join-Path $env:LOCALAPPDATA "STS2_ModHotReload\staging\$ModId"
$modsDir = Join-Path (Get-Sts2ModsPath) $ModId
if (-not $modsDir) {
    Require-Sts2Path | Out-Null
    $modsDir = Join-Path (Get-Sts2ModsPath) $ModId
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null

if (-not $DllPath) {
    $candidates = @(
        (Join-Path $modsDir "$ModId.dll.pending"),
        (Join-Path $modsDir "$ModId.dll")
    )
    $DllPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $DllPath -or -not (Test-Path $DllPath)) {
    throw "DLL not found. Pass -DllPath or build first (may produce .dll.pending)."
}

$dest = Join-Path $staging "$ModId.dll"
Copy-Item $DllPath $dest -Force
Write-Host "[push-staging] $DllPath -> $dest" -ForegroundColor Green
(Get-Item $dest).LastWriteTimeUtc = [DateTime]::UtcNow

if (-not $NoTouchJson) {
    $json = Join-Path $modsDir "$ModId.json"
    if (Test-Path $json) {
        (Get-Item $json).LastWriteTime = Get-Date
        Write-Host "[push-staging] touched $json (~2s until reload)" -ForegroundColor Cyan
    } else {
        Write-Host "[push-staging] no $json; use in-game console: reload $ModId" -ForegroundColor Yellow
    }
} else {
    Write-Host "[push-staging] use in-game console: reload $ModId" -ForegroundColor Yellow
}
