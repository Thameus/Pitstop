param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repo = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repo "src\Pitstop.Cli\bin\$Configuration\net10.0\pit.dll"
if (-not (Test-Path $dll)) { throw "pit.dll not found: $dll (run dotnet build first)" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet not found in PATH' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("pitstop-smoke-" + [Guid]::NewGuid().ToString('N'))
$proc = $null
$http = $null
$oldRoot = $env:PIT_RAIZ
$oldAuto = $env:PIT_SEM_AUTOSTART
$oldBrowser = $env:RUNNER_ABRIR_NAVEGADOR

try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Copy-Item -Recurse (Join-Path $repo 'web') (Join-Path $tmp 'web')

    $env:PIT_RAIZ = $tmp
    $env:PIT_SEM_AUTOSTART = '1'
    $env:RUNNER_ABRIR_NAVEGADOR = 'false'

    & dotnet $dll status | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "pit status failed with exit code $LASTEXITCODE" }

    $cfgPath = Join-Path $tmp 'config\perfis.json'
    if (-not (Test-Path $cfgPath)) { throw 'status did not create config/perfis.json' }
    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($null -eq $cfg.perfis) { throw 'generated config does not contain perfis' }

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = (Get-Command dotnet).Source
    $psi.UseShellExecute = $false
    $psi.Arguments = '"' + $dll + '" ui ' + $port
    $psi.Environment['PIT_RAIZ'] = $tmp
    $psi.Environment['PIT_SEM_AUTOSTART'] = '1'
    $psi.Environment['RUNNER_ABRIR_NAVEGADOR'] = 'false'
    $proc = [Diagnostics.Process]::Start($psi)

    $http = [Net.Http.HttpClient]::new()
    $http.Timeout = [TimeSpan]::FromSeconds(2)
    $base = "http://127.0.0.1:$port"
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        if ($proc.HasExited) { throw "pit ui exited early with code $($proc.ExitCode)" }
        try {
            $response = $http.GetAsync("$base/api/status").GetAwaiter().GetResult()
            if ([int]$response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'HTTP server did not become ready' }

    $rootResponse = $http.GetAsync("$base/").GetAwaiter().GetResult()
    if ([int]$rootResponse.StatusCode -ne 200) { throw "GET / returned $([int]$rootResponse.StatusCode)" }
    if ($rootResponse.Headers.GetValues('X-Content-Type-Options') -notcontains 'nosniff') { throw 'GET / missing X-Content-Type-Options: nosniff' }
    if (($rootResponse.Headers.GetValues('Content-Security-Policy') -join ';') -notmatch "frame-ancestors 'none'") { throw "GET / missing CSP frame-ancestors 'none'" }
    if ($rootResponse.Headers.GetValues('Referrer-Policy') -notcontains 'no-referrer') { throw 'GET / missing Referrer-Policy: no-referrer' }

    $badHost = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "$base/api/status")
    $badHost.Headers.Host = 'example.invalid'
    $badHostResponse = $http.SendAsync($badHost).GetAwaiter().GetResult()
    if ([int]$badHostResponse.StatusCode -ne 403) { throw "invalid Host returned $([int]$badHostResponse.StatusCode), expected 403" }

    $put = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Put, "$base/api/cfg")
    $put.Content = [Net.Http.StringContent]::new('{"perfis":{}}', [Text.Encoding]::UTF8, 'application/json')
    $putResponse = $http.SendAsync($put).GetAwaiter().GetResult()
    if ([int]$putResponse.StatusCode -ne 403) { throw "PUT without X-PIT returned $([int]$putResponse.StatusCode), expected 403" }

    $scriptsNoHeader = $http.GetAsync("$base/api/scripts?perfil=inexistente").GetAwaiter().GetResult()
    if ([int]$scriptsNoHeader.StatusCode -ne 403) { throw "GET /api/scripts without X-PIT returned $([int]$scriptsNoHeader.StatusCode), expected 403" }

    $scriptsInvalid = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, "$base/api/scripts?perfil=..")
    $scriptsInvalid.Headers.Add('X-PIT', '1')
    $scriptsInvalidResponse = $http.SendAsync($scriptsInvalid).GetAwaiter().GetResult()
    if ([int]$scriptsInvalidResponse.StatusCode -ne 400) { throw "GET /api/scripts with unsafe profile returned $([int]$scriptsInvalidResponse.StatusCode), expected 400" }

    $unsafe = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Put, "$base/api/cfg")
    $unsafe.Headers.Add('X-PIT', '1')
    $unsafe.Content = [Net.Http.StringContent]::new('{"perfis":{"..":{}}}', [Text.Encoding]::UTF8, 'application/json')
    $unsafeResponse = $http.SendAsync($unsafe).GetAwaiter().GetResult()
    if ([int]$unsafeResponse.StatusCode -ne 400) { throw "unsafe profile name returned $([int]$unsafeResponse.StatusCode), expected 400" }
    $cfgAfterUnsafe = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($cfgAfterUnsafe.perfis.PSObject.Properties.Name -contains '..') { throw 'unsafe profile name was persisted' }

    $exit = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$base/api/sair?rapido=1")
    $exit.Headers.Add('X-PIT', '1')
    $exit.Content = [Net.Http.StringContent]::new('')
    $exitResponse = $http.SendAsync($exit).GetAwaiter().GetResult()
    if ([int]$exitResponse.StatusCode -ne 200) { throw "POST /api/sair returned $([int]$exitResponse.StatusCode)" }

    if (-not $proc.WaitForExit(10000)) { throw 'pit ui did not exit after /api/sair' }
    if ($proc.ExitCode -ne 0) { throw "pit ui exited with code $($proc.ExitCode)" }

    Write-Host 'SMOKE OK: CLI, isolated config, HTTP API protections and clean shutdown.' -ForegroundColor Green
}
finally {
    if ($http) { $http.Dispose() }
    if ($proc -and -not $proc.HasExited) {
        try { $proc.Kill($true); $proc.WaitForExit(3000) | Out-Null } catch { }
    }
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    $env:PIT_RAIZ = $oldRoot
    $env:PIT_SEM_AUTOSTART = $oldAuto
    $env:RUNNER_ABRIR_NAVEGADOR = $oldBrowser
}
