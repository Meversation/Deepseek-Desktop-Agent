@echo off
chcp 65001 >nul
setlocal
set "SCRIPT=%~dp0run.ps1"

where pwsh >nul 2>nul
if errorlevel 1 (set "PS=powershell") else (set "PS=pwsh")

start "" %PS% -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%SCRIPT%" %*

pause
