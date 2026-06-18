@echo off
title Better HSR-Currency Wars V4 - Preview

cd /d "%~dp0"

echo ==========================================
echo  Better HSR-Currency Wars V4 - Preview
echo ==========================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found in PATH.
    echo Please install .NET Desktop Runtime or SDK.
    echo.
    pause
    exit /b 1
)

echo Building Release...
dotnet build HsrCurrencyWarsCleanWpf.csproj -c Release -v minimal
if errorlevel 1 (
    echo.
    echo Build failed.
    echo.
    pause
    exit /b 1
)

echo.
echo Starting with dotnet DLL host...
echo This avoids the framework-dependent EXE runtime prompt.
echo.
dotnet "bin\Release\net10.0-windows\Better HSR-Currency Wars V4.dll"

echo.
echo Program exited.
pause
