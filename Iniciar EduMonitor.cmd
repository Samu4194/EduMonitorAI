@echo off
cd /d "%~dp0"
rem Por defecto TODO corre en Docker, incluida la base de datos (SQL Server en un contenedor): no hay que instalar nada mas.
rem Solo si existe el archivo .local\usar-express.txt se usa el SQL Server instalado en Windows (compose.express.yaml).
rem No mezcles los dos modos: cada uno guarda sus datos en una base distinta.
if exist "%~dp0.local\usar-express.txt" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Express.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start.ps1"
)
if errorlevel 1 pause
