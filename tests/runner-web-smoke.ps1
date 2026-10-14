param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repoPit = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repoPit "src\Pitstop.Cli\bin\$Configuration\net10.0\pit.dll"
if (-not (Test-Path $dll)) { throw "compile a CLI antes do teste: $dll" }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("pitstop-tomcat-e2e-" + [Guid]::NewGuid().ToString('N'))
$proc = $null
$http = [Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(5)

function Livre() {
    $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $l.Start()
    $port = ([Net.IPEndPoint]$l.LocalEndpoint).Port
    $l.Stop()
    return $port
}
function API([string]$nome, [string]$acao) {
    $req = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$base/api/p/$nome/$acao")
    $req.Headers.Add('X-PIT','1')
    $req.Content = [Net.Http.StringContent]::new('')
    try {
        $res = $http.SendAsync($req).GetAwaiter().GetResult()
        if ([int]$res.StatusCode -ne 200) {
            $txt = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            throw "$acao status=$([int]$res.StatusCode) $txt"
        }
    } finally { $req.Dispose() }
}
function Estado() {
    $todos = $http.GetStringAsync("$base/api/status").GetAwaiter().GetResult() | ConvertFrom-Json
    return @($todos | Where-Object nome -eq 'webtest') | Select-Object -First 1
}
function Aguardar([scriptblock]$predicado, [string]$erro, [int]$tentativas=50) {
    for($i=0; $i -lt $tentativas; $i++) {
        if (& $predicado) { return }
        Start-Sleep -Milliseconds 250
    }
    throw $erro
}

try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Copy-Item -Recurse (Join-Path $repoPit 'web') (Join-Path $tmp 'web')
    # Executa uma cópia isolada para não bloquear o build da CLI no repositório.
    $runtime = Join-Path $tmp 'runtime'
    Copy-Item -Recurse (Split-Path $dll -Parent) $runtime
    $dllTeste = Join-Path $runtime 'pit.dll'
    $repo = Join-Path $tmp 'repo'
    $tomcat = Join-Path $tmp 'tomcat'
    $maven = Join-Path $tmp 'maven'
    $webSrc = Join-Path $repo 'src\main\webapp'
    $webOut = Join-Path $repo 'target\app'
    New-Item -ItemType Directory -Force $webSrc, (Join-Path $maven 'bin'), (Join-Path $tomcat 'bin'), (Join-Path $tomcat 'conf') | Out-Null
    Set-Content -Path (Join-Path $repo 'pom.xml') -Encoding UTF8 -Value '<project><modelVersion>4.0.0</modelVersion><groupId>test</groupId><artifactId>webtest</artifactId><version>1</version><packaging>war</packaging></project>'
    $httpPort = Livre
    $runnerPort = Livre
    while($runnerPort -eq $httpPort) { $runnerPort = Livre }
    $mvn = @'
@echo off
if exist "fail.flag" (
  echo [ERROR] BUILD FAILURE
  exit /b 1
)
if not exist "target\app\WEB-INF" mkdir "target\app\WEB-INF"
if not exist "target\classes" mkdir "target\classes"
echo ok>>"build-count.txt"
echo [INFO] BUILD SUCCESS
exit /b 0
'@
    [IO.File]::WriteAllText((Join-Path $maven 'bin\mvn.cmd'), $mvn, [Text.Encoding]::ASCII)
    $serverJs = @'
const http = require("http");
const port = Number(process.argv[2]);
console.log("Server startup in 120ms");
setTimeout(() => {
  const server = http.createServer((req, res) => {
    if (req.url === "/shutdown") { res.end("bye"); server.close(() => process.exit(0)); return; }
    res.writeHead(200, {"content-type": "text/plain"}); res.end("ok");
  });
  server.listen(port, "127.0.0.1");
}, 1800);
'@
    [IO.File]::WriteAllText((Join-Path $tomcat 'server.js'), $serverJs)
    $cat = @'
@echo off
if "%1"=="stop" (
    node -e "require('http').get('http://127.0.0.1:PORT/shutdown', r=>r.resume()).on('error',()=>{})"
    exit /b 0
)
node "SERVER_JS" PORT
'@
    $cat = $cat.Replace('PORT', [string]$httpPort).Replace('SERVER_JS', (Join-Path $tomcat 'server.js'))
    [IO.File]::WriteAllText((Join-Path $tomcat 'bin\catalina.bat'), $cat, [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText((Join-Path $tomcat 'conf\server.xml'),
        '<Server port="8005"><Service name="Catalina"><Connector port="8080" protocol="HTTP/1.1" /></Service></Server>')
    $cfg = @{
        perfis = @{
            webtest = @{
                porta=$httpPort; portaShutdown=($httpPort-75)
                tomcatHome=$tomcat; javaHome=$tomcat; mavenHome=$maven
                prontoUrl="http://localhost:$httpPort/health"
                prepararAoIniciar=$true; syncAutomatico=$true
                artefatos=@(@{ativo=$true;repo=$repo;modulo='';contexto='/app';build='mvn package';
                    sync=@(@{de='src/main/webapp';para=''})})
            }
        }
    }
    New-Item -ItemType Directory -Force (Join-Path $tmp 'config') | Out-Null
    [IO.File]::WriteAllText((Join-Path $tmp 'config\perfis.json'), ($cfg | ConvertTo-Json -Depth 12), [Text.Encoding]::UTF8)
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = (Get-Command dotnet).Source
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.Arguments = '"' + $dllTeste + '" ui ' + $runnerPort
    $psi.Environment['PIT_RAIZ'] = $tmp
    $psi.Environment['PIT_SEM_AUTOSTART'] = '1'
    $psi.Environment['RUNNER_ABRIR_NAVEGADOR'] = 'false'
    $psi.Environment['STOP_TIMEOUT_SEG'] = '3'
    $proc = [Diagnostics.Process]::Start($psi)
    $base = "http://127.0.0.1:$runnerPort"
    Aguardar { try { $null=$http.GetStringAsync("$base/api/status").GetAwaiter().GetResult(); $true } catch { $false } } 'Runner HTTP não iniciou'

    API 'webtest' 'start'
    Aguardar { (Test-Path (Join-Path $repo 'build-count.txt')) } 'Maven simulado não foi chamado'
    Aguardar { $s=Estado; $s.execucao -and $s.execucao.pronto } 'Tomcat/healthcheck não marcou pronto' 80
    if (@(Get-Content (Join-Path $repo 'build-count.txt')).Count -ne 1) { throw 'primeiro start compilou mais de uma vez' }
    Write-Host 'E2E OK primeira inicialização Maven -> Tomcat -> HTTP'

    $origem = Join-Path $webSrc 'index.html'
    $destino = Join-Path $webOut 'index.html'
    Set-Content -Path $origem -Encoding UTF8 -Value 'web-pitstop-test'
    Aguardar { Test-Path $destino } 'Sync automático não copiou novo arquivo' 40
    Remove-Item $origem
    Aguardar { -not (Test-Path $destino) } 'Sync automático não removeu arquivo gerenciado' 40
    Write-Host 'E2E OK sync automático copia e remove'

    API 'webtest' 'stop'
    Aguardar { $s=Estado; -not $s.execucao -and -not $s.emUso } 'Tomcat não parou' 40
    API 'webtest' 'start'
    Aguardar { $s=Estado; $s.execucao -and $s.execucao.pronto } 'segundo start não concluiu' 80
    if (@(Get-Content (Join-Path $repo 'build-count.txt')).Count -ne 1) { throw 'segundo start reconstruiu sem mudanças' }
    Write-Host 'E2E OK segunda inicialização pula Maven'

    API 'webtest' 'stop'
    Aguardar { $s=Estado; -not $s.execucao -and -not $s.emUso } 'segunda parada falhou' 40
    Set-Content -Path (Join-Path $repo 'fail.flag') -Value 'true'
    API 'webtest' 'start'
    Aguardar { $s=Estado; $s.ultimo -and $s.ultimo.tipo -eq 'build' -and -not $s.ultimo.ok } 'build falhou mas não reportou falha' 80
    $ultimo = Estado
    if ($ultimo.execucao -and $ultimo.execucao.tipo -in @('tomcat','debug')) { throw 'Tomcat iniciou após Maven falhar' }
    Write-Host 'E2E OK build falho impede início'
    Write-Host 'RUNNER WEB E2E OK' -ForegroundColor Green
}
finally {
    if($proc -and -not $proc.HasExited) { try { $proc.Kill($true); $proc.WaitForExit(3000) | Out-Null } catch {} }
    # Alguns hosts de terminal deixam um runner filho vivo depois de encerrar o processo inicial.
    # Encerrar exclusivamente processos que usam esta raiz temporária, nunca instâncias reais.
    Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('dotnet.exe','node.exe') -and $_.CommandLine -like ('*' + $tmp + '*') } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }
    $http.Dispose()
    if(Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
