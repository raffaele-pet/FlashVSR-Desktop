@echo off
setlocal DisableDelayedExpansion
rem Works from any current directory; the app need not be inside this ZIP.
start "" powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File "%~dp0scripts\RepositoryLauncher.ps1" -Action Launch
exit /b
