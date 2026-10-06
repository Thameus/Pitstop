<#
  Remove o Pitstop instalado pelo instalar.ps1: atalhos, "pit" do PATH, entrada em Aplicativos instalados, o
  "iniciar com o Windows" e os arquivos do programa. Seus dados (.env, config\, cache\, logs\, bases\) só saem se
  você confirmar.
#>
param([switch]$Silencioso, [switch]$ApagarDados)
$ErrorActionPreference = 'Stop'
$pasta = $PSScriptRoot
$exe = Join-Path $pasta 'app\Pitstop.exe'

function Pergunta([string]$texto, [bool]$padrao) {
    if ($Silencioso) { return $padrao }
    $op = if ($padrao) { '[S/n]' } else { '[s/N]' }
    $r = Read-Host "$texto $op"
    if ([string]::IsNullOrWhiteSpace($r)) { return $padrao }
    return $r -match '^(s|sim|y|yes)$'
}

Write-Host "`n  Pitstop - desinstalação de $pasta`n" -ForegroundColor Magenta
$aberto = Get-Process -Name 'Pitstop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($aberto) {
    Write-Host '  O Pitstop está aberto. Feche pelo ícone da bandeja (Parar e Sair) para continuar.' -ForegroundColor Yellow
    while (Get-Process -Id $aberto.Id -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
}

foreach ($d in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
    $l = Join-Path $d 'Pitstop.lnk'
    if (Test-Path $l) { Remove-Item $l -Force }
}
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$v = (Get-ItemProperty $run -ErrorAction SilentlyContinue).Pitstop
if ($v -and $v -like "*$exe*") { Remove-ItemProperty $run -Name 'Pitstop' }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Pitstop' -Recurse -Force -ErrorAction SilentlyContinue
$pathUser = [Environment]::GetEnvironmentVariable('Path', 'User')
$novo = (@($pathUser -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ne $pasta.TrimEnd('\') })) -join ';'
if ($novo -ne $pathUser) { [Environment]::SetEnvironmentVariable('Path', $novo, 'User') }

$dados = @('.env', 'config', 'cache', 'logs', 'bases')
$apagarDados = $ApagarDados -or (Pergunta '  Apagar também seus perfis e ajustes (.env, config, cache, logs, bases)?' $false)
Get-ChildItem $pasta -Force | Where-Object { $apagarDados -or ($dados -notcontains $_.Name) } | ForEach-Object {
    if ($_.FullName -ne $PSCommandPath) { Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue }
}
# o próprio script sai por último (depois que este processo terminar)
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & del /q `"$PSCommandPath`" & rmdir `"$pasta`" 2>nul" -WindowStyle Hidden
Write-Host "  Pitstop removido.$(if (-not $apagarDados) { " Seus dados ficaram em $pasta." })" -ForegroundColor Green
