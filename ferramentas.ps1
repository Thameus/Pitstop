<# Ferramentas opcionais do Pitstop. Instala versões portáteis sem administrador e grava os caminhos no .env. #>
param(
    [Parameter(Mandatory=$true)][string]$Destino,
    [switch]$BaixarJdk,
    [switch]$BaixarMaven,
    [ValidateSet('', '9', '10', '11')][string]$BaixarTomcat = '',
    [switch]$BaixarNode,
    [string]$JdkHome = '',
    [string]$MavenHome = '',
    [string]$TomcatHome = '',
    [string]$NodeHome = '',
    [string]$ProjetosDir = ''
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Destino = [IO.Path]::GetFullPath($Destino)
$Tools = Join-Path $Destino 'tools'
$Tmp = Join-Path ([IO.Path]::GetTempPath()) ('pitstop-tools-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $Tools, $Tmp | Out-Null

function Baixar([string]$Url, [string]$Arquivo) {
    Write-Host "  Baixando $Url" -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Arquivo
}
function Hash-Esperado([string]$Url, [int]$Tamanho) {
    $txt = (Invoke-WebRequest -UseBasicParsing -Uri $Url).Content
    $m = [regex]::Match($txt, "(?i)\b[0-9a-f]{$Tamanho}\b")
    if (-not $m.Success) { throw "checksum inválido em $Url" }
    return $m.Value.ToLowerInvariant()
}
function Verificar([string]$Arquivo, [string]$Algoritmo, [string]$Esperado) {
    $real = (Get-FileHash $Arquivo -Algorithm $Algoritmo).Hash.ToLowerInvariant()
    if ($real -ne $Esperado.ToLowerInvariant()) { throw "checksum não confere: $(Split-Path $Arquivo -Leaf)" }
}
function Extrair-Zip([string]$Zip, [string]$Alvo) {
    $extr = Join-Path $Tmp ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $extr | Out-Null
    Expand-Archive -Path $Zip -DestinationPath $extr -Force
    if (Test-Path $Alvo) { Remove-Item -Recurse -Force $Alvo }
    $itens = @(Get-ChildItem $extr -Force)
    if ($itens.Count -eq 1 -and $itens[0].PSIsContainer) {
        Move-Item $itens[0].FullName $Alvo
    } else {
        New-Item -ItemType Directory -Force $Alvo | Out-Null
        foreach ($i in $itens) { Move-Item $i.FullName $Alvo }
    }
}
function Upsert-Env([string]$Chave, [string]$Valor) {
    if ([string]::IsNullOrWhiteSpace($Valor)) { return }
    $arq = Join-Path $Destino '.env'
    $modelo = Join-Path $Destino '.env.exemplo'
    if (-not (Test-Path $arq)) {
        if (Test-Path $modelo) { Copy-Item $modelo $arq }
        else { Set-Content -Encoding UTF8 $arq '# Pitstop - ajustes globais' }
    }
    $linhas = @(Get-Content $arq)
    $re = '^\s*#?\s*' + [regex]::Escape($Chave) + '\s*='
    $nova = "$Chave=$Valor"
    $achou = $false
    for ($i=0; $i -lt $linhas.Count; $i++) {
        if ($linhas[$i] -match $re) { $linhas[$i] = $nova; $achou = $true; break }
    }
    if (-not $achou) { $linhas += $nova }
    [IO.File]::WriteAllLines($arq, $linhas, (New-Object Text.UTF8Encoding($false)))
}
function Validar-Pasta([string]$Tipo, [string]$Pasta) {
    if ([string]::IsNullOrWhiteSpace($Pasta)) { return '' }
    $Pasta = [IO.Path]::GetFullPath($Pasta.Trim('"'))
    $ok = switch ($Tipo) {
        'jdk'    { Test-Path (Join-Path $Pasta 'bin\java.exe') }
        'maven'  { Test-Path (Join-Path $Pasta 'bin\mvn.cmd') }
        'tomcat' { Test-Path (Join-Path $Pasta 'bin\catalina.bat') }
        'node'   { (Test-Path (Join-Path $Pasta 'node.exe')) -or (Test-Path (Join-Path $Pasta 'bin\node.exe')) }
    }
    if (-not $ok) { throw "${Tipo}: pasta inválida: $Pasta" }
    return $Pasta
}

try {
    if ($BaixarJdk) {
        Write-Host '  Preparando Eclipse Temurin JDK 21...' -ForegroundColor Magenta
        $api = 'https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse'
        $assets = @(Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent'='Pitstop-Setup' })
        if ($assets.Count -eq 0) { throw 'Adoptium não retornou JDK 21 para Windows x64' }
        $pkg = $assets[0].binary.package
        $zip = Join-Path $Tmp 'jdk.zip'
        Baixar $pkg.link $zip
        if ($pkg.checksum) { Verificar $zip 'SHA256' $pkg.checksum }
        $JdkHome = Join-Path $Tools 'jdk-21'
        Extrair-Zip $zip $JdkHome
        Write-Host "  JDK instalado: $JdkHome" -ForegroundColor Green
    } else { $JdkHome = Validar-Pasta 'jdk' $JdkHome }

    if ($BaixarMaven) {
        $ver = '3.10.0'
        Write-Host "  Preparando Apache Maven $ver..." -ForegroundColor Magenta
        $nome = "apache-maven-$ver-bin.zip"
        $url = "https://dlcdn.apache.org/maven/maven-3/$ver/binaries/$nome"
        $zip = Join-Path $Tmp $nome
        Baixar $url $zip
        Verificar $zip 'SHA512' (Hash-Esperado ($url + '.sha512') 128)
        $MavenHome = Join-Path $Tools 'maven'
        Extrair-Zip $zip $MavenHome
        Write-Host "  Maven instalado: $MavenHome" -ForegroundColor Green
    } else { $MavenHome = Validar-Pasta 'maven' $MavenHome }

    if ($BaixarTomcat) {
        $versoes = @{ '9'='9.0.122'; '10'='10.1.60'; '11'='11.0.26' }
        $ver = $versoes[$BaixarTomcat]
        Write-Host "  Preparando Apache Tomcat $ver..." -ForegroundColor Magenta
        $nome = "apache-tomcat-$ver.zip"
        $url = "https://dlcdn.apache.org/tomcat/tomcat-$BaixarTomcat/v$ver/bin/$nome"
        $zip = Join-Path $Tmp $nome
        Baixar $url $zip
        Verificar $zip 'SHA512' (Hash-Esperado ($url + '.sha512') 128)
        $TomcatHome = Join-Path $Tools ("tomcat-" + $BaixarTomcat)
        Extrair-Zip $zip $TomcatHome
        Write-Host "  Tomcat instalado: $TomcatHome" -ForegroundColor Green
    } else { $TomcatHome = Validar-Pasta 'tomcat' $TomcatHome }
    if ($BaixarNode) {
        Write-Host '  Preparando Node.js LTS...' -ForegroundColor Magenta
        $indice = @(Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json' -Headers @{ 'User-Agent'='Pitstop-Setup' })
        $node = $indice | Where-Object { $_.lts -and $_.lts -ne $false } | Select-Object -First 1
        if (-not $node) { throw 'nodejs.org não retornou uma versão LTS' }
        $ver = [string]$node.version
        $nome = "node-$ver-win-x64.zip"
        $base = "https://nodejs.org/dist/$ver"
        $zip = Join-Path $Tmp $nome
        Baixar "$base/$nome" $zip
        $somas = (Invoke-WebRequest -UseBasicParsing -Uri "$base/SHASUMS256.txt").Content -split [Environment]::NewLine
        $linha = $somas | Where-Object { $_ -match ('\s+\*?' + [regex]::Escape($nome) + '\s*$') } | Select-Object -First 1
        if (-not $linha -or $linha -notmatch '^([0-9a-fA-F]{64})') { throw 'checksum do Node.js não encontrado' }
        Verificar $zip 'SHA256' $matches[1]
        $NodeHome = Join-Path $Tools 'node'
        Extrair-Zip $zip $NodeHome
        Write-Host "  Node.js instalado: $NodeHome" -ForegroundColor Green
    } else { $NodeHome = Validar-Pasta 'node' $NodeHome }

    if ($ProjetosDir) {
        $ProjetosDir = [IO.Path]::GetFullPath($ProjetosDir.Trim('"'))
        if (-not (Test-Path $ProjetosDir -PathType Container)) { throw "pasta de projetos não existe: $ProjetosDir" }
    }

    Upsert-Env 'JDK_HOME' $JdkHome
    Upsert-Env 'MAVEN_HOME' $MavenHome
    Upsert-Env 'TOMCAT_HOME' $TomcatHome
    Upsert-Env 'NODE_HOME' $NodeHome
    Upsert-Env 'PROJETOS_DIR' $ProjetosDir

    Write-Host ''
    Write-Host '  Ferramentas configuradas.' -ForegroundColor Green
    if ($JdkHome)    { Write-Host "    JDK:    $JdkHome" }
    if ($MavenHome)  { Write-Host "    Maven:  $MavenHome" }
    if ($TomcatHome) { Write-Host "    Tomcat: $TomcatHome" }
    if ($NodeHome)   { Write-Host "    Node:   $NodeHome" }
}
finally {
    Remove-Item -Recurse -Force $Tmp -ErrorAction SilentlyContinue
}
