# Launch STS2 in-process integration tests; parse itest-results.json
param(
    [int]$TimeoutSec = 420,
    [switch]$SkipLaunch,
    [switch]$LiveCombat,
    [string]$Sts2Path = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

$root = Split-Path $PSScriptRoot -Parent
if (-not $Sts2Path) {
    $Sts2Path = Get-Sts2InstallPath
}
if (-not $Sts2Path) { throw "STS2 not found. Set STS2_PATH." }

$exe = Join-Path $Sts2Path "SlayTheSpire2.exe"
$itestRoot = Join-Path $env:LOCALAPPDATA "STS2_ModHotReload"
$flagFile = Join-Path $itestRoot "run-itest.flag"
$resultsFile = Join-Path $itestRoot "itest-results.json"
$godotLog = Join-Path $env:APPDATA "SlayTheSpire2\logs\godot.log"
$steamAppId = "2868840"

function Write-Step([string]$msg) { Write-Host "[itest] $msg" }

function Ensure-SteamAppId {
    $f = Join-Path $Sts2Path "steam_appid.txt"
    if (-not (Test-Path $f)) {
        Set-Content -Path $f -Value $steamAppId -Encoding ASCII
        Write-Step "created steam_appid.txt ($steamAppId) for offline launch"
    }
}

function Ensure-ModsEnabledInSettings {
    param([string[]]$ModIds = @("ModHotReload", "BaseLib"))
    $settings = Find-Sts2SettingsSave
    if (-not $settings) {
        Write-Step "WARN: no settings.save (bootstrap patch still forces mod agree)"
        return
    }
    try {
        $json = Get-Content $settings -Raw | ConvertFrom-Json
        if (-not $json.mod_settings) { return }
        $json.mod_settings.mods_enabled = $true
        foreach ($m in $ModIds) {
            $entry = $json.mod_settings.mod_list | Where-Object { $_.id -eq $m }
            if ($entry) { $entry.is_enabled = $true }
        }
        $json | ConvertTo-Json -Depth 10 | Set-Content $settings -Encoding UTF8
        Write-Step "enabled mods in settings.save: $($ModIds -join ', ')"
    }
    catch {
        Write-Step "WARN: could not patch settings.save: $($_.Exception.Message)"
    }
}

if (-not $SkipLaunch) {
    if (-not (Test-Path $exe)) { throw "game exe not found: $exe" }

    Write-Step "build + verify..."
    & (Join-Path $root "scripts\verify-build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "verify-build failed" }

    Ensure-SteamAppId
    Ensure-ModsEnabledInSettings

    New-Item -ItemType Directory -Force -Path $itestRoot | Out-Null
    Remove-Item $resultsFile -ErrorAction SilentlyContinue
    Set-Content -Path $flagFile -Value (Get-Date).ToString("o") -Encoding UTF8

    $env:STS2_MODHOTRELOAD_ITEST = "1"
    $env:STS2_MODHOTRELOAD_ITEST_QUIT = "1"
    if ($LiveCombat) { $env:STS2_MODHOTRELOAD_ITEST_LIVE = "1" }

    Write-Step "launch game (timeout ${TimeoutSec}s)"
    $proc = Start-Process -FilePath $exe -WorkingDirectory $Sts2Path -PassThru

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $resultsFile) { break }
        if ($proc.HasExited) {
            Write-Step "game exited early, wait for results..."
            Start-Sleep -Seconds 3
            break
        }
        Start-Sleep -Seconds 2
    }

    if (-not $proc.HasExited) {
        Write-Step "stopping game"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    Remove-Item Env:STS2_MODHOTRELOAD_ITEST -ErrorAction SilentlyContinue
    Remove-Item Env:STS2_MODHOTRELOAD_ITEST_QUIT -ErrorAction SilentlyContinue
    Remove-Item Env:STS2_MODHOTRELOAD_ITEST_LIVE -ErrorAction SilentlyContinue
}

if (-not (Test-Path $resultsFile)) {
    if (Test-Path $godotLog) {
        Write-Step "godot.log [ITEST]/[热重载] tail:"
        Select-String -Path $godotLog -Pattern "\[ITEST\]|Mod Hot Reload|RUNNING MODDED|Skipping loading mod" |
            Select-Object -Last 40 | ForEach-Object { $_.Line }
    }
    throw "missing results: $resultsFile (check steam_appid.txt and mod enablement)"
}

$report = Get-Content $resultsFile -Raw | ConvertFrom-Json
Write-Step "version $($report.Version) pass=$($report.Passed) fail=$($report.Failed) skip=$($report.Skipped)"
foreach ($s in $report.Scenarios) {
    $mark = switch ($s.Status) { "pass" { "+" } "fail" { "!" } default { "-" } }
    Write-Host "  $mark $($s.Name): $($s.Detail)"
}

if ($report.Failed -gt 0) {
    throw "integration test failed: $($report.Failed) scenario(s)"
}

Write-Step "all passed"
exit 0
