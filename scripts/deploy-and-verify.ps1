# Build, deploy, patch sts2.runtimeconfig.json, verify.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

$proj = Join-Path $root "ModHotReload.csproj"
$verifyProj = Join-Path $root "tools\ModHotReloadVerify\ModHotReloadVerify.csproj"
$mods = Get-ModHotReloadDeployPath
if (-not $mods) { Require-Sts2Path | Out-Null; $mods = Get-ModHotReloadDeployPath }

Write-Host "=== dotnet build Release ===" -ForegroundColor Cyan
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $mods "ModHotReload.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    Write-Error "Deploy failed: $dll missing (set DeployToGame=true and valid Sts2Path)"
}

Write-Host "=== patch sts2.runtimeconfig.json ===" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "patch-runtimeconfig.ps1")

$dataDir = Get-Sts2DataDir
Write-Host "=== ModHotReloadVerify ===" -ForegroundColor Cyan
dotnet run --project $verifyProj -c Release -- $dll $dataDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "OK: normal Steam launch works (restart game once if it was running)." -ForegroundColor Green
