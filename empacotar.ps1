<#
  Gera os pacotes de distribuição do Pitstop em dist\:
    pitstop-<versão>-win-x64.tar.xz e pitstop-<versão>-linux-x64.tar.xz
  Cada um traz o .NET embutido (self-contained: quem baixa não precisa instalar nada além do que os perfis usam),
  a tela web, o instalador e as licenças. Não leva nada desta máquina: .env, config\, cache\, logs\, bases\ ficam fora.

  Uso:  powershell -ExecutionPolicy Bypass -File empacotar.ps1 [-Rids win-x64,linux-x64]
        [-Assinar -Certificado <impressão digital SHA1> [-Timestamp <url>]]

  Assinatura (desligada por padrão): com -Assinar, o Pitstop.exe e o pit.exe do pacote Windows são assinados
  (Authenticode) com o signtool antes de compactar. O certificado precisa estar no repositório de certificados do
  Windows (token USB ou o app da nuvem da autoridade, ex.: SimplySign Desktop da Certum, já o colocam lá). Ver a
  impressão digital: certmgr.msc → Pessoal → Certificados → o certificado → Detalhes → Impressão digital.
  O signtool vem no Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100) ou informe -Signtool <caminho>.
  Precisa: .NET 10 SDK e o tar.exe do Windows 10/11 (bsdtar com xz). O bit de execução dos arquivos do Linux vai
  num manifesto mtree (o NTFS não guarda esse bit).
#>
param(
    [string[]]$Rids = @('win-x64', 'linux-x64'),
    [switch]$Assinar,
    [string]$Certificado = $env:PITSTOP_CERT_SHA1,
    [string]$Timestamp = 'http://timestamp.sectigo.com',
    [string]$Signtool = ''
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$versao = ([xml](Get-Content 'src\Directory.Build.props')).Project.PropertyGroup.Version
if (-not $versao) { throw 'versão não encontrada em src\Directory.Build.props' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet não encontrado no PATH (instale o .NET 10 SDK)' }
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
if (-not (Test-Path $tar)) { throw 'tar.exe do Windows não encontrado' }

if ($Assinar) {
    if (-not $Certificado) { throw '-Assinar precisa de -Certificado <impressão digital SHA1> (ou PITSTOP_CERT_SHA1 no ambiente)' }
    $Certificado = ($Certificado -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if (-not $Signtool) {
        $Signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
        if (-not $Signtool) {
            $Signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (-not $Signtool -or -not (Test-Path $Signtool)) { throw 'signtool.exe não encontrado: instale o Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100) ou informe -Signtool' }
    if (-not (Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My | Where-Object Thumbprint -eq $Certificado)) {
        throw "certificado $Certificado não está no repositório do Windows (token conectado? app da nuvem aberto?)"
    }
    Write-Host "Assinatura ligada: certificado $Certificado, timestamp $Timestamp" -ForegroundColor Yellow
}

$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $dist 'stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
$somas = Join-Path $dist 'SHA256SUMS.txt'
if (Test-Path $somas) { Remove-Item $somas }

# arquivos da raiz que vão no pacote (o resto da raiz é desta máquina ou de desenvolvimento)
$raizComum = @('.env.exemplo', 'LICENCAS.md', 'THIRD-PARTY-NOTICES.md', 'LICENSE', 'NOTICE')
$porSo = @{
    'win-x64'   = @('instalar.cmd', 'instalar.ps1', 'desinstalar.ps1', 'ferramentas.ps1', 'pit.cmd')
    'linux-x64' = @('instalar.sh', 'desinstalar.sh', 'pit')
}

function Escapar-Mtree([string]$s) {
    # mtree: espaço, # e \ viram octal
    ($s.ToCharArray() | ForEach-Object {
        if ($_ -eq ' ' -or $_ -eq '#' -or $_ -eq '\') { '\{0}' -f [Convert]::ToString([int][char]$_, 8).PadLeft(3, '0') } else { $_ }
    }) -join ''
}

function Adicionar-Hash([string]$arquivo) {
    $hash = (Get-FileHash $arquivo -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $arquivo -Leaf)" | Add-Content $somas
}

function Gerar-SetupWindows([string]$dir, [string]$nome) {
    $payload = Join-Path $stage "$nome-payload.zip"
    $build = Join-Path $stage "$nome-setup"
    $buildBase = Join-Path $stage "$nome-setup-bin"
    if (Test-Path $payload) { Remove-Item $payload -Force }
    if (Test-Path $build) { Remove-Item $build -Recurse -Force }
    if (Test-Path $buildBase) { Remove-Item $buildBase -Recurse -Force }
    $items = @(Get-ChildItem $dir -Force)
    Compress-Archive -Path $items.FullName -DestinationPath $payload -CompressionLevel Optimal
    $publishArgs = @('publish', 'src\Pitstop.Setup.Windows\Pitstop.Setup.Windows.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $build, '--nologo', '-v', 'q', '-p:DebugType=none', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', "-p:BaseOutputPath=$buildBase\", "-p:PayloadPath=$payload")
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'publish do Pitstop-Setup.exe falhou' }
    $exe = Join-Path $build 'Pitstop-Setup.exe'
    if (-not (Test-Path $exe)) { throw 'Pitstop-Setup.exe não foi gerado' }
    if ($Assinar) {
        & $Signtool sign /sha1 $Certificado /fd sha256 /tr $Timestamp /td sha256 /d 'Pitstop Setup' $exe
        if ($LASTEXITCODE -ne 0) { throw 'assinatura do Pitstop-Setup.exe falhou' }
        & $Signtool verify /pa /q $exe
        if ($LASTEXITCODE -ne 0) { throw 'verificação do Pitstop-Setup.exe falhou' }
    }
    $saidaSetup = Join-Path $dist "pitstop-$versao-setup-win-x64.exe"
    Copy-Item $exe $saidaSetup -Force
    Remove-Item $payload -Force
    Remove-Item $build -Recurse -Force
    Remove-Item $buildBase -Recurse -Force -ErrorAction SilentlyContinue
    return $saidaSetup
}

function Gerar-RunLinux([string]$arquivoTar) {
    $modelo = Get-Content (Join-Path $PSScriptRoot 'installer\pitstop-linux.run.sh') -Raw
    $crlf = ([string][char]13) + ([string][char]10)
    $lf = [string][char]10
    $modelo = $modelo.Replace('@VERSION@', $versao).Replace($crlf, $lf)
    if (-not $modelo.EndsWith($lf)) { $modelo += $lf }
    $saidaRun = Join-Path $dist "pitstop-$versao-linux-x64.run"
    if (Test-Path $saidaRun) { Remove-Item $saidaRun -Force }
    [IO.File]::WriteAllText($saidaRun, $modelo, (New-Object Text.UTF8Encoding($false)))
    $out = [IO.File]::Open($saidaRun, [IO.FileMode]::Append, [IO.FileAccess]::Write)
    try {
        $input = [IO.File]::OpenRead($arquivoTar)
        try { $input.CopyTo($out) } finally { $input.Dispose() }
    } finally { $out.Dispose() }
    return $saidaRun
}

foreach ($rid in $Rids) {
    $nome = "pitstop-$versao-$rid"
    $dir = Join-Path $stage $nome
    $app = Join-Path $dir 'app'
    Write-Host "== $nome" -ForegroundColor Cyan
    # Linux: sem dependência de libicu (as datas e números da tela têm formato fixo)
    $extra = @()
    if ($rid -like 'linux-*') { $extra += '-p:InvariantGlobalization=true' }
    foreach ($proj in @('src\Pitstop.App\Pitstop.App.csproj', 'src\Pitstop.Cli\Pitstop.Cli.csproj')) {
        & dotnet publish $proj -c Release -r $rid --self-contained true -o $app --nologo -v q -p:DebugType=none -p:GenerateDocumentationFile=false @extra
        if ($LASTEXITCODE -ne 0) { throw "publish falhou: $proj ($rid)" }
    }
    if ($Assinar -and $rid -like 'win-*') {
        # timestamp: a assinatura continua válida depois que o certificado vencer
        $exes = @((Join-Path $app 'Pitstop.exe'), (Join-Path $app 'pit.exe'))
        & $Signtool sign /sha1 $Certificado /fd sha256 /tr $Timestamp /td sha256 /d 'Pitstop' $exes
        if ($LASTEXITCODE -ne 0) { throw 'signtool sign falhou' }
        & $Signtool verify /pa /q $exes
        if ($LASTEXITCODE -ne 0) { throw 'signtool verify falhou' }
        Write-Host '   Pitstop.exe e pit.exe assinados'
    }
    Copy-Item -Recurse (Join-Path $PSScriptRoot 'web') (Join-Path $dir 'web')
    Copy-Item -Recurse (Join-Path $PSScriptRoot 'third-party') (Join-Path $dir 'third-party')
    foreach ($f in $raizComum + $porSo[$rid]) { Copy-Item (Join-Path $PSScriptRoot $f) $dir }
    Copy-Item (Join-Path $PSScriptRoot 'LEIAME.md') (Join-Path $dir 'LEIAME.md')
    Get-ChildItem $app -Filter '*.pdb' -File | Remove-Item

    # manifesto mtree: diretórios 0755; executáveis do Linux e scripts 0755; o resto 0644
    $exec = @('app/Pitstop', 'app/pit', 'app/createdump', 'instalar.sh', 'desinstalar.sh', 'pit')
    $agora = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $linhas = New-Object System.Collections.Generic.List[string]
    $linhas.Add('#mtree')
    $linhas.Add("./$nome type=dir mode=0755 time=$agora.0")
    Get-ChildItem $dir -Recurse | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($dir.Length + 1).Replace('\', '/')
        $caminho = Escapar-Mtree "./$nome/$rel"
        if ($_.PSIsContainer) { $linhas.Add("$caminho type=dir mode=0755 time=$agora.0") }
        else {
            $modo = if ($rid -like 'linux-*' -and ($exec -contains $rel -or $rel -like '*.so')) { '0755' } else { '0644' }
            $t = [DateTimeOffset]::new($_.LastWriteTimeUtc).ToUnixTimeSeconds()
            $linhas.Add("$caminho type=file mode=$modo time=$t.0 contents=$(Escapar-Mtree $_.FullName.Replace('\', '/'))")
        }
    }
    $mtree = Join-Path $stage "$nome.mtree"
    [IO.File]::WriteAllLines($mtree, $linhas)
    $saida = Join-Path $dist "$nome.tar.xz"
    if (Test-Path $saida) { Remove-Item $saida }
    Push-Location $stage
    & $tar --options xz:compression-level=9 -cJf $saida "@$nome.mtree"
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) { throw "tar falhou ($rid)" }
    $mb = [math]::Round((Get-Item $saida).Length / 1MB, 1)
    Adicionar-Hash $saida
    Write-Host "   $saida ($mb MB)" -ForegroundColor Green

    if ($rid -eq 'win-x64') {
        $setupExe = Gerar-SetupWindows $dir $nome
        Adicionar-Hash $setupExe
        $setupMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
        Write-Host "   $setupExe ($setupMb MB) - instalador recomendado" -ForegroundColor Green
    }
    if ($rid -eq 'linux-x64') {
        $runFile = Gerar-RunLinux $saida
        Adicionar-Hash $runFile
        $runMb = [math]::Round((Get-Item $runFile).Length / 1MB, 1)
        Write-Host "   $runFile ($runMb MB) - instalador recomendado" -ForegroundColor Green
    }
}
Remove-Item -Recurse -Force $stage
Write-Host "Pronto. Somas em dist\SHA256SUMS.txt"
