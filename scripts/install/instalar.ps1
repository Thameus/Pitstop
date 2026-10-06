<#
  Instalador do Pitstop para Windows (sem administrador).
  Copia esta pasta para %LOCALAPPDATA%\Programs\Pitstop (ou -Destino), cria o atalho no Menu Iniciar (e, se
  quiser, na Área de Trabalho), põe o "pit" no PATH do usuário, registra em "Aplicativos instalados" e abre o
  Pitstop. Reinstalar/atualizar mantém o que é seu: .env, config\, cache\, logs\ e bases\.
  Uso: duplo clique em instalar.cmd, ou: powershell -ExecutionPolicy Bypass -File instalar.ps1 [-Destino <pasta>] [-Silencioso]
#>
param(
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'Programs\Pitstop'),
    [switch]$Silencioso,
    [switch]$Desktop,
    [switch]$SemPath,
    [switch]$NaoAbrir,
    [switch]$SemMenu,
    [switch]$SemRegistro,
    [string]$Origem = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$origem = [IO.Path]::GetFullPath($Origem)

function Pergunta([string]$texto, [bool]$padrao = $true) {
    if ($Silencioso) { return $padrao }
    $op = if ($padrao) { '[S/n]' } else { '[s/N]' }
    $r = Read-Host "$texto $op"
    if ([string]::IsNullOrWhiteSpace($r)) { return $padrao }
    return $r -match '^(s|sim|y|yes)$'
}

Write-Host ''
Write-Host '  Pitstop - instalação' -ForegroundColor Magenta
Write-Host '  Runner local de Tomcat, Java, npm e comandos personalizados.'
Write-Host ''

if (-not (Test-Path (Join-Path $origem 'app\Pitstop.exe'))) { throw "app\Pitstop.exe não encontrado em $origem (rode o instalador de dentro da pasta extraída)" }
if ([Environment]::Is64BitOperatingSystem -eq $false) { throw 'o Pitstop precisa do Windows 64 bits' }

if (-not $Silencioso) {
    $r = Read-Host "  Pasta de instalação [$Destino]"
    if (-not [string]::IsNullOrWhiteSpace($r)) { $Destino = $r.Trim('"') }
}
$Destino = [IO.Path]::GetFullPath($Destino)

# Pitstop aberto a partir do destino: o Windows trava o .exe em uso
$aberto = Get-Process -Name 'Pitstop' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Destino, [StringComparison]::OrdinalIgnoreCase) }
if ($aberto) {
    Write-Host '  O Pitstop está aberto. Feche pelo ícone da bandeja (Parar e Sair) para continuar.' -ForegroundColor Yellow
    if ($Silencioso) { throw 'Pitstop aberto' }
    while (Get-Process -Id $aberto.Id -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
}

if ($origem.TrimEnd('\') -ne $Destino.TrimEnd('\')) {
    Write-Host "  Copiando para $Destino ..."
    if (Test-Path -LiteralPath $Destino) {
        if (-not (Test-Path -LiteralPath $Destino -PathType Container)) {
            throw "Destino existe, mas não é uma pasta: $Destino"
        }
    } else {
        New-Item -ItemType Directory -Force $Destino | Out-Null
    }
    # app\ e web\ são do programa: troca inteiro (versão nova não deixa arquivo velho para trás)
    foreach ($d in @('app', 'web')) {
        $alvo = Join-Path $Destino $d
        if (Test-Path $alvo) { Remove-Item -Recurse -Force $alvo }
        Copy-Item -Recurse (Join-Path $origem $d) $alvo
    }
    foreach ($f in Get-ChildItem $origem -File) { Copy-Item $f.FullName (Join-Path $Destino $f.Name) -Force }

    # No repositório os auxiliares ficam organizados em scripts/install; no pacote já vêm achatados na raiz.
    $scriptsInstall = Join-Path $origem 'scripts\install'
    if (Test-Path $scriptsInstall -PathType Container) {
        foreach ($nome in @('instalar.ps1', 'desinstalar.ps1', 'ferramentas.ps1')) {
            $fonte = Join-Path $scriptsInstall $nome
            if (Test-Path $fonte) { Copy-Item $fonte (Join-Path $Destino $nome) -Force }
        }
    }
}

$exe = Join-Path $Destino 'app\Pitstop.exe'
$versao = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''

# atalhos (Menu Iniciar sempre; Área de Trabalho se quiser)
$ws = New-Object -ComObject WScript.Shell
function Atalho([string]$pasta) {
    $l = $ws.CreateShortcut((Join-Path $pasta 'Pitstop.lnk'))
    $l.TargetPath = $exe
    $l.WorkingDirectory = Split-Path $exe
    $l.IconLocation = "$exe,0"
    $l.Description = 'Pitstop - Tomcat, Java, npm e comandos com um clique'
    $l.Save()
}
if (-not $SemMenu) { Atalho ([Environment]::GetFolderPath('Programs')) }
$criarDesktop = if ($SemMenu) { $false } elseif ($Silencioso) { $Desktop } else { Pergunta '  Criar atalho na Área de Trabalho?' $false }
if ($criarDesktop) { Atalho ([Environment]::GetFolderPath('Desktop')) }

# pit no PATH do usuário
$pathUser = [Environment]::GetEnvironmentVariable('Path', 'User')
$partes = @($pathUser -split ';' | Where-Object { $_ })
if ($partes -notcontains $Destino) {
    $porNoPath = if ($Silencioso) { -not $SemPath } else { Pergunta '  Pôr o comando "pit" no PATH (terminal: pit status, pit up <perfil>)?' $true }
    if ($porNoPath) {
        [Environment]::SetEnvironmentVariable('Path', (($partes + $Destino) -join ';'), 'User')
        Write-Host '  PATH atualizado (vale nos terminais abertos daqui para frente).'
    }
}

# "Aplicativos instalados" (HKCU, sem administrador)
if (-not $SemRegistro) {
    $chave = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Pitstop'
    New-Item -Path $chave -Force | Out-Null
    $desinstalar = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $Destino 'desinstalar.ps1')`""
    $valores = @{
        DisplayName = 'Pitstop'; DisplayVersion = $versao; DisplayIcon = "$exe,0"; InstallLocation = $Destino
        UninstallString = $desinstalar; NoModify = 1; NoRepair = 1
        Comments = 'Runner local de Tomcat, Java, npm e comandos personalizados'
    }
    foreach ($k in $valores.Keys) {
        $tipo = if ($valores[$k] -is [int]) { 'DWord' } else { 'String' }
        New-ItemProperty -Path $chave -Name $k -Value $valores[$k] -PropertyType $tipo -Force | Out-Null
    }
    $kb = [math]::Round(((Get-ChildItem $Destino -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB)
    New-ItemProperty -Path $chave -Name 'EstimatedSize' -Value ([int]$kb) -PropertyType DWord -Force | Out-Null
}

Write-Host ''
Write-Host "  Pitstop $versao instalado em $Destino" -ForegroundColor Green
Write-Host '  Na primeira abertura um assistente pergunta os caminhos padrão (JDK, Tomcat, Maven) - tudo opcional.'
Write-Host '  Precisa de JDK, Tomcat, Maven ou Node? São gratuitos: adoptium.net, tomcat.apache.org, maven.apache.org, nodejs.org'
Write-Host ''
if (-not $NaoAbrir -and (Pergunta '  Abrir o Pitstop agora?' $true)) { Start-Process $exe }
