# Optional: Rien mod combat smoke (requires Rien + BaseLib installed).
param(
    [int]$TimeoutSec = 600,
    [switch]$SkipLaunch,
    [switch]$SkipBuild,
    [string]$Sts2Path = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

$root = Split-Path $PSScriptRoot -Parent
if (-not $Sts2Path) { $Sts2Path = Get-Sts2InstallPath }
if (-not $Sts2Path) { throw "STS2 not found. Set STS2_PATH." }

$exe = Join-Path $Sts2Path "SlayTheSpire2.exe"
$verifyRoot = Join-Path $env:LOCALAPPDATA "STS2_ModHotReload"
$flagFile = Join-Path $verifyRoot "run-rien-combat-verify.flag"
$resultsFile = Join-Path $verifyRoot "rien-combat-verify-results.json"
$screenshotFile = Join-Path $verifyRoot "rien-combat-verify.png"
$godotLog = Join-Path $env:APPDATA "SlayTheSpire2\logs\godot.log"
$steamAppId = "2868840"

function Write-Step([string]$msg) { Write-Host "[rcv] $msg" -ForegroundColor Cyan }

function Ensure-SteamAppId {
    $f = Join-Path $Sts2Path "steam_appid.txt"
    if (-not (Test-Path $f)) {
        Set-Content -Path $f -Value $steamAppId -Encoding ASCII
        Write-Step "created steam_appid.txt"
    }
}

function Ensure-ModsEnabledInSettings {
    $settings = Find-Sts2SettingsSave
    if (-not $settings) {
        Write-Step "WARN: no settings.save (bootstrap still forces mod agree)"
        return
    }
    try {
        $json = Get-Content $settings -Raw | ConvertFrom-Json
        if (-not $json.mod_settings) { return }
        $json.mod_settings.mods_enabled = $true
        foreach ($m in @("ModHotReload", "BaseLib", "Rien")) {
            $entry = $json.mod_settings.mod_list | Where-Object { $_.id -eq $m }
            if ($entry) { $entry.is_enabled = $true }
        }
        $json | ConvertTo-Json -Depth 10 | Set-Content $settings -Encoding UTF8
        Write-Step "enabled ModHotReload/BaseLib/Rien in settings.save"
    }
    catch {
        Write-Step "WARN: settings.save patch failed: $($_.Exception.Message)"
    }
}

if (-not $SkipBuild) {
    Write-Step "build ModHotReload..."
    Push-Location $root
    try {
        dotnet build -c Release | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
    }
    finally { Pop-Location }
}

if (-not $SkipLaunch) {
    if (-not (Test-Path $exe)) { throw "game exe not found: $exe" }

    Ensure-SteamAppId
    Ensure-ModsEnabledInSettings

    New-Item -ItemType Directory -Force -Path $verifyRoot | Out-Null
    Remove-Item $resultsFile -ErrorAction SilentlyContinue
    Remove-Item $screenshotFile -ErrorAction SilentlyContinue
    Set-Content -Path $flagFile -Value (Get-Date).ToString("o") -Encoding UTF8

    $env:STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY = "1"
    $env:STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY_QUIT = "1"

    Write-Step "launch game (timeout ${TimeoutSec}s)"
    $proc = Start-Process -FilePath $exe -WorkingDirectory $Sts2Path -PassThru

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $resultsFile) { break }
        if ($proc.HasExited) {
            Write-Step "game exited early, waiting for results..."
            Start-Sleep -Seconds 5
            break
        }
        Start-Sleep -Seconds 2
    }

    if (-not $proc.HasExited) {
        Write-Step "stopping game"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    Remove-Item Env:STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY -ErrorAction SilentlyContinue
    Remove-Item Env:STS2_MODHOTRELOAD_RIEN_COMBAT_VERIFY_QUIT -ErrorAction SilentlyContinue
}

if (-not (Test-Path $resultsFile)) {
    if (Test-Path $godotLog) {
        Write-Step "godot.log [RCV] tail:"
        Select-String -Path $godotLog -Pattern "\[RCV\]|RienCombatVerify|EnterRoomDebug|RIENCOMBATVERIFY|Mod Hot Reload" |
            Select-Object -Last 50 | ForEach-Object { $_.Line }
    }
    throw "missing results: $resultsFile"
}

$report = Get-Content $resultsFile -Raw | ConvertFrom-Json
Write-Step "version $($report.Version) pass=$($report.Passed) fail=$($report.Failed) skip=$($report.Skipped)"
if ($report.ScreenshotPath) { Write-Step "screenshot: $($report.ScreenshotPath)" }
if ($report.RienLogPath) { Write-Step "rien log: $($report.RienLogPath)" }

foreach ($s in $report.Scenarios) {
    $mark = switch ($s.Status) { "pass" { "+" } "fail" { "!" } default { "-" } }
    Write-Host "  $mark $($s.Name): $($s.Detail)"
}

if ($report.Failed -gt 0) {
    throw "rien combat verify failed: $($report.Failed) scenario(s)"
}

Write-Step "all passed"
exit 0
