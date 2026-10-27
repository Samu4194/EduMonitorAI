@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Stop.ps1" -StopDatabase
if errorlevel 1 pause
