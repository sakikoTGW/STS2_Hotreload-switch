# Optional: build + deploy + patch sts2.runtimeconfig.json (normal Steam launch works after this).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot "deploy-and-verify.ps1")
& (Join-Path $PSScriptRoot "patch-runtimeconfig.ps1")
