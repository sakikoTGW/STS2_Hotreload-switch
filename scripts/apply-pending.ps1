# After closing the game: sync build output to mods\ModHotReload\ (incl. Core + StartupHook).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

$mods = Get-ModHotReloadDeployPath
if (-not $mods) {
    Require-Sts2Path | Out-Null
    $mods = Get-ModHotReloadDeployPath
}
$buildDir = Join-Path $root ".godot\mono\temp\bin\Release"

$toCopy = @(
    "ModHotReload.dll",
    "ModHotReload.Core.dll",
    "ModHotReload.StartupHook.dll",
    "ModHotReload.pck"
)

foreach ($name in $toCopy) {
    $pending = Join-Path $mods ($name + ".pending")
    $fromBuild = Join-Path $buildDir $name
    $target = Join-Path $mods $name

    if (Test-Path $pending) {
        Copy-Item $pending $target -Force
        Remove-Item $pending -Force
        Write-Host "Applied pending: $name"
        continue
    }

    if ($name -eq "ModHotReload.dll" -and (Test-Path (Join-Path $mods "ModHotReload.dll.pending"))) {
        Copy-Item (Join-Path $mods "ModHotReload.dll.pending") $target -Force
        Remove-Item (Join-Path $mods "ModHotReload.dll.pending") -Force
        Write-Host "Applied pending: ModHotReload.dll"
    }

    if (Test-Path $fromBuild) {
        Copy-Item $fromBuild $target -Force
        Write-Host "Copied from build: $name"
    }
}

if (-not (Test-Path (Join-Path $mods "ModHotReload.Core.dll"))) {
    Write-Error "Missing ModHotReload.Core.dll — run: dotnet build ModHotReload.csproj -c Release"
}

Write-Host "Deploy done: $mods"
