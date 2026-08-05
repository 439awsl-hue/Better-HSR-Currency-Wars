@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0log" mkdir "%~dp0log"
set "HSR_APP_LOG_DIR=%~dp0log"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" -Build
if errorlevel 1 pause
endlocal
