@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-portable.ps1" %*
echo.
pause
