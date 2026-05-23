# Writes startupHooks into sts2.runtimeconfig.json (pure PowerShell).
param([string]$Sts2Path = $env:STS2_PATH)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

if ([string]::IsNullOrWhiteSpace($Sts2Path)) {
    $Sts2Path = Get-Sts2InstallPath
}
if (-not $Sts2Path) { throw "STS2 not found. Set STS2_PATH or install via Steam." }

$mods = Join-Path $Sts2Path "mods\ModHotReload"
$hook = Join-Path $mods "ModHotReload.StartupHook.dll"
$dataDir = Get-Sts2DataDir -Sts2Path $Sts2Path
if (-not $dataDir) { throw "STS2 data folder not found under $Sts2Path" }
$config = Join-Path $dataDir "sts2.runtimeconfig.json"

if (-not (Test-Path -LiteralPath $hook)) { throw "Missing $hook — build and deploy ModHotReload first." }
if (-not (Test-Path -LiteralPath $config)) { throw "Missing $config" }

$hookEntry = ((Resolve-Path -LiteralPath $hook).Path) -replace '\\', '/'

$raw = Get-Content -LiteralPath $config -Raw -Encoding UTF8
if ($raw -match 'ModHotReload\.StartupHook\.dll') {
    Write-Host "[ModHotReload] runtimeconfig already has StartupHook."
    exit 0
}

$backup = "$config.bak"
if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $config -Destination $backup
}

if ($raw -match '"startupHooks"\s*:\s*\[') {
    $raw = $raw -replace '("startupHooks"\s*:\s*\[)', "`$1`n      `"$hookEntry`","
} else {
    $raw = $raw -replace '("configProperties"\s*:\s*\{)', "`"startupHooks`": [`n      `"$hookEntry`"`n    ],`n    `$1"
}

Set-Content -LiteralPath $config -Value $raw -Encoding UTF8 -NoNewline
Write-Host "[ModHotReload] startupHooks -> $hookEntry"
Write-Host "[ModHotReload] Normal Steam launch is enough (restart game if it is running)."
