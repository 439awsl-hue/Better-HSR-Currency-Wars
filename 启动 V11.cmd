@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -Build
if errorlevel 1 pause
endlocal
