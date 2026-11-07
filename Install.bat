@echo off
setlocal DisableDelayedExpansion
rem Double-click from the cloned or downloaded repository folder.
start "" powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File "%~dp0scripts\RepositoryLauncher.ps1" -Action Install
exit /b
