@echo off
cd /d "%~dp0"
rem Con compose.express.yaml se usa el SQL Server instalado en Windows; si no existe, SQL Server en Docker.
if exist "%~dp0compose.express.yaml" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Express.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start.ps1"
)
if errorlevel 1 pause
