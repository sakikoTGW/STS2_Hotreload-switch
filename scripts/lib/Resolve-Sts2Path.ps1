# Shared STS2 / mods path resolution (no hardcoded drive letters).
function Get-Sts2InstallPath {
    param([string]$Override = $env:STS2_PATH)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($Override)) { $candidates += $Override }

    $reg = Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue
    if ($reg -and $reg.SteamPath) {
        $candidates += (Join-Path $reg.SteamPath "steamapps\common\Slay the Spire 2")
    }

    if ($IsLinux) {
        $candidates += "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2"
    }
    elseif ($IsMacOS) {
        $candidates += "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
    }
    else {
        $candidates += "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
        foreach ($lib in @("E", "D", "F", "G")) {
            $candidates += "${lib}:\SteamLibrary\steamapps\common\Slay the Spire 2"
        }
    }

    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        if (Test-Path -LiteralPath $c) {
            return (Resolve-Path -LiteralPath $c).Path
        }
    }

    return $null
}

function Get-Sts2ModsPath {
    param([string]$Sts2Path = $(Get-Sts2InstallPath))
    if (-not $Sts2Path) { return $null }
    return Join-Path $Sts2Path "mods"
}

function Get-ModHotReloadDeployPath {
    param([string]$Sts2Path = $(Get-Sts2InstallPath))
    $mods = Get-Sts2ModsPath -Sts2Path $Sts2Path
    if (-not $mods) { return $null }
    return Join-Path $mods "ModHotReload"
}

function Get-Sts2DataDir {
    param([string]$Sts2Path = $(Get-Sts2InstallPath))
    if (-not $Sts2Path) { return $null }
    $names = @(
        "data_sts2_windows_x86_64",
        "data_sts2_linuxbsd_x86_64",
        "data_sts2_macos_x86_64"
    )
    foreach ($n in $names) {
        $p = Join-Path $Sts2Path $n
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

function Find-Sts2SettingsSave {
    $steamRoot = Join-Path $env:APPDATA "SlayTheSpire2\steam"
    if (-not (Test-Path $steamRoot)) { return $null }
    $found = Get-ChildItem -Path $steamRoot -Recurse -Filter "settings.save" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($found) { return $found.FullName }
    return $null
}

function Require-Sts2Path {
    $p = Get-Sts2InstallPath
    if (-not $p) {
        throw @"
Slay the Spire 2 install not found.
Set environment variable STS2_PATH to your game folder, e.g.:
  `$env:STS2_PATH = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'
"@
    }
    return $p
}
