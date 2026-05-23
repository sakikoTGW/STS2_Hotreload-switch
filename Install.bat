@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"
if errorlevel 1 (
  echo Install failed.
  pause
  exit /b 1
)
echo.
echo Done. Launch Slay the Spire 2 from Steam and enable Mod Hot Reload in the mod list.
pause
