@echo off
setlocal DisableDelayedExpansion
rem Launch the installed application from any current directory.
start "" powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File "%~dp0scripts\RepositoryLauncher.ps1" -Action Launch
exit /b
