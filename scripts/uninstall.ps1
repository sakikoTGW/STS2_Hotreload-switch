# Remove Steam launch hook only (keeps mod files).
$ErrorActionPreference = "Stop"
$appId = "2868840"
$keyPath = "HKCU:\Software\Valve\Steam\Apps\$appId"
if (-not (Test-Path $keyPath)) { exit 0 }
$existing = (Get-ItemProperty -Path $keyPath -Name LaunchOptions -ErrorAction SilentlyContinue).LaunchOptions
if (-not $existing) { exit 0 }
if ($existing -notmatch "sts2-launch\.cmd") { exit 0 }
$clean = $existing -replace '"[^"]*sts2-launch\.cmd"\s*', '' -replace '\s+', ' ' -trim
if ($clean -eq '%command%' -or [string]::IsNullOrWhiteSpace($clean)) {
    Remove-ItemProperty -Path $keyPath -Name LaunchOptions -ErrorAction SilentlyContinue
} else {
    Set-ItemProperty -Path $keyPath -Name LaunchOptions -Value $clean
}
Write-Host "Removed ModHotReload from Steam launch options."
