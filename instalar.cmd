@echo off
rem Instalador do Pitstop (Windows, sem administrador): duplo clique aqui.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
if errorlevel 1 (echo. & echo Instalacao FALHOU. & pause & exit /b 1)
pause
