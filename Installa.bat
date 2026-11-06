@echo off
setlocal DisableDelayedExpansion
rem Double-click after extracting the complete repository ZIP.
start "" powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File "%~dp0scripts\RepositoryLauncher.ps1" -Action Install
exit /b
