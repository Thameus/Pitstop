@echo off
rem Linha de comando do Pitstop: pit status | pit up <perfil> [--debug] [--build] | pit down <perfil> | pit build <perfil> | pit ui | pit ajustes
if not exist "%~dp0app\pit.exe" (echo app\pit.exe nao encontrado. Rode build.cmd ^(desenvolvimento^) ou o instalador. & exit /b 1)
"%~dp0app\pit.exe" %*
