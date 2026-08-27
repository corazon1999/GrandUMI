@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0qq-bug-bot\export-live-qq-whitelist.ps1"
set "GRANDUMI_EXPORT_EXIT=%ERRORLEVEL%"
echo.
if /I "%~1"=="-NoPause" goto :finished
pause
:finished
exit /b %GRANDUMI_EXPORT_EXIT%
