# Build + assembly smoke test + optional deploy size check.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot "lib\Resolve-Sts2Path.ps1")

Push-Location $root
try {
    dotnet build -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    $dll = Join-Path $root ".godot\mono\temp\bin\$Configuration\ModHotReload.dll"
    if (-not (Test-Path $dll)) {
        throw "Build output not found: $dll"
    }

    $dataDir = Get-Sts2DataDir
    if (-not $dataDir) {
        Require-Sts2Path | Out-Null
        $dataDir = Get-Sts2DataDir
    }
    if (-not $dataDir) { throw "STS2 data dir not found (set STS2_PATH)" }

    $verifyProj = Join-Path $root "tools\ModHotReloadVerify\ModHotReloadVerify.csproj"
    dotnet run --project $verifyProj -- $dll $dataDir
    if ($LASTEXITCODE -ne 0) { throw "ModHotReloadVerify failed" }

    $modsDll = Join-Path (Get-ModHotReloadDeployPath) "ModHotReload.dll"
    if (Test-Path $modsDll) {
        $built = Get-Item $dll
        $deployed = Get-Item $modsDll
        Write-Host "mods deploy: $($deployed.Length) bytes, $($deployed.LastWriteTime)"
        $pendingPath = "$modsDll.pending"
        if ($built.Length -ne $deployed.Length -and (Test-Path $pendingPath)) {
            Write-Host "WARN: game may lock DLL; run apply-pending.ps1 after exit"
        }
    } else {
        Write-Host "WARN: ModHotReload.dll not in mods folder (DeployToGame or copy manually)"
    }

    Write-Host ""
    Write-Host "verify-build: done"
}
finally {
    Pop-Location
}
