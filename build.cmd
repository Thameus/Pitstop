@echo off
rem Desenvolvimento: publica o Pitstop em app\ (Pitstop.exe + pit.exe e as .dll) e cria o atalho "Pitstop" nesta pasta e no
rem Menu Iniciar. Para gerar os pacotes de distribuicao (.tar.xz Windows e Linux): empacotar.ps1.
rem Precisa do .NET 10 SDK: winget install Microsoft.DotNet.SDK.10
setlocal
cd /d "%~dp0"

set "DOTNET=dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
  ) else (
    echo dotnet nao encontrado no PATH nem em "%ProgramFiles%\dotnet\dotnet.exe".
    echo Instale o .NET 10 SDK.
    pause
    exit /b 1
  )
)

rem o Windows trava o exe em uso: o Pitstop precisa estar fechado (menu da bandeja - Parar e Sair)
tasklist /FI "IMAGENAME eq Pitstop.exe" /NH | find /I "Pitstop.exe" >nul && (
  echo O Pitstop esta aberto. Feche pelo menu da bandeja ^(Parar e Sair^) e rode de novo.
  pause
  exit /b 1
)

if exist app rmdir /s /q app
"%DOTNET%" publish src\Pitstop.App\Pitstop.App.csproj -c Release -o app --nologo || goto erro
"%DOTNET%" publish src\Pitstop.Cli\Pitstop.Cli.csproj -c Release -o app --nologo || goto erro

rem atalhos: duplo clique abre o Pitstop (janela + icone na bandeja)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$exe = Join-Path '%~dp0' 'app\Pitstop.exe';" ^
  "$ws = New-Object -ComObject WScript.Shell;" ^
  "foreach ($dir in @('%~dp0', [Environment]::GetFolderPath('Programs'))) {" ^
  "  $l = $ws.CreateShortcut((Join-Path $dir 'Pitstop.lnk'));" ^
  "  $l.TargetPath = $exe; $l.WorkingDirectory = (Split-Path $exe); $l.IconLocation = $exe + ',0';" ^
  "  $l.Description = 'Pitstop'; $l.Save() }" || goto erro

echo.
echo Pronto. Abra pelo atalho "Pitstop" (aqui ou no Menu Iniciar).
echo Linha de comando: pit.cmd
start "" "app\Pitstop.exe"
exit /b 0

:erro
echo.
echo Build FALHOU.
pause
exit /b 1
