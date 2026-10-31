@echo off
chcp 65001 >nul
setlocal
set "SCRIPT=%~dp0build.ps1"

where pwsh >nul 2>nul
if errorlevel 1 (set "PS=powershell") else (set "PS=pwsh")

%PS% -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "CODE=%errorlevel%"

echo.
pause
exit /b %CODE%
