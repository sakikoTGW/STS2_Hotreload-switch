# Static + deploy checks for hot reload (game not required for verify step).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

$buildDll = Join-Path $root ".godot\mono\temp\bin\Release\ModHotReload.dll"
$deployDll = Join-Path (Get-ModHotReloadDeployPath) "ModHotReload.dll"
$dataDir = Get-Sts2DataDir
$config = if ($dataDir) { Join-Path $dataDir "sts2.runtimeconfig.json" } else { $null }
$verifyProj = Join-Path $root "tools\ModHotReloadVerify\ModHotReloadVerify.csproj"

Write-Host "=== build ===" -ForegroundColor Cyan
dotnet build (Join-Path $root "ModHotReload.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path $buildDll)) {
    Write-Error "Build output missing: $buildDll"
}

Write-Host "=== ModHotReloadVerify + audit ===" -ForegroundColor Cyan
if (-not $dataDir) {
    Write-Warning "STS2 data dir not found; skipping verify (set STS2_PATH)"
} else {
    dotnet run --project $verifyProj -c Release -- $buildDll $dataDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ((Test-Path $deployDll) -and (Test-Path $buildDll)) {
    $b = (Get-Item $buildDll).Length
    $d = (Get-Item $deployDll).Length
    if ($b -ne $d) {
        Write-Host "WARN: deploy DLL size $d != build $b (exit game and run deploy-and-verify.ps1)" -ForegroundColor Yellow
    } else {
        Write-Host "OK: deploy DLL matches build ($b bytes)" -ForegroundColor Green
    }
}

if ($config -and (Test-Path $config)) {
    $raw = Get-Content $config -Raw
    if ($raw -match 'ModHotReload\.StartupHook') {
        Write-Host "OK: sts2.runtimeconfig.json has StartupHook" -ForegroundColor Green
    } else {
        Write-Host "WARN: runtimeconfig missing StartupHook (play once or run patch-runtimeconfig.ps1)" -ForegroundColor Yellow
    }
}

$log = Join-Path $env:LOCALAPPDATA "STS2_ModHotReload\startup-hook.log"
if (Test-Path $log) {
    Write-Host "=== last startup-hook.log (tail 5) ===" -ForegroundColor Cyan
    Get-Content $log -Tail 5
}

Write-Host ""
Write-Host "In-game test: create $env:LOCALAPPDATA\STS2_ModHotReload\run-itest.flag then launch game" -ForegroundColor Cyan
