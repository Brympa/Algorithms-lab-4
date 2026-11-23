@echo off
setlocal DisableDelayedExpansion

set "app=%~dp0src\AlgorithmsLab.Desktop\bin\Release\net10.0-windows\AlgorithmsLab.Desktop.exe"
if /i "%~1"=="--build" goto build
if exist "%app%" goto launch

:build
where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo The .NET 10 SDK is required to build this project.
    pause
    exit /b 1
)
echo Building Algorithms Lab...
dotnet build "%~dp0src\AlgorithmsLab.Desktop\AlgorithmsLab.Desktop.csproj" -c Release --nologo -m:1 -p:UseSharedCompilation=false
if errorlevel 1 goto failed

:launch
if not exist "%app%" goto failed
start "" "%app%"
if errorlevel 1 goto failed
exit /b 0

:failed
echo Failed to build or launch Algorithms Lab.
pause
exit /b 1
