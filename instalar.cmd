@echo off
rem Instalador do Pitstop (Windows, sem administrador): funciona no pacote e no repositorio.
if exist "%~dp0instalar.ps1" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install\instalar.ps1" -Origem "%~dp0" %*
)
if errorlevel 1 (echo. & echo Instalacao FALHOU. & pause & exit /b 1)
pause
