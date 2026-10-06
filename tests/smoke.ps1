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

    # Perfil Comando: execução, mesma sessão, fail-fast, fallback da pasta e precedência .env < env do perfil.
    $work = Join-Path $tmp 'command-work'
    New-Item -ItemType Directory -Force $work | Out-Null
    $envFile = Join-Path $tmp 'command.env'
    Set-Content -Path $envFile -Value 'PIT_ENV_TEST=from-file' -Encoding UTF8

    $isWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    $sameSession = if ($isWindows) { "set PIT_SAME=works`necho %PIT_SAME%" } else { "PIT_SAME=works`necho `"`$PIT_SAME`"" }
    $envEcho = if ($isWindows) { 'echo %PIT_ENV_TEST%' } else { 'echo "$PIT_ENV_TEST"' }
    $cwdEcho = if ($isWindows) { 'cd' } else { 'pwd' }
    $failFast = if ($isWindows) { "echo before`ncmd /d /c exit 7`necho after" } else { "echo before`nfalse`necho after" }
    $longRun = if ($isWindows) { 'ping 127.0.0.1 -t >nul' } else { 'sleep 60' }

    $testCfg = [ordered]@{
        projetosDir = $work
        perfis = [ordered]@{
            simple = [ordered]@{ tipo = 'comando'; comando = "echo stdout-ok`necho stderr-ok 1>&2"; shell = 'auto' }
            session = [ordered]@{ tipo = 'comando'; comando = $sameSession; shell = 'auto' }
            env = [ordered]@{ tipo = 'comando'; comando = $envEcho; shell = 'auto'; envArquivo = $envFile; env = 'PIT_ENV_TEST=inline' }
            fallback = [ordered]@{ tipo = 'comando'; comando = $cwdEcho; shell = 'auto' }
            failfast = [ordered]@{ tipo = 'comando'; comando = $failFast; shell = 'auto' }
            longrun = [ordered]@{ tipo = 'comando'; comando = $longRun; shell = 'auto' }
            timeout = [ordered]@{ tipo = 'comando'; comando = $longRun; shell = 'auto'; timeoutExecucaoSeg = 1 }
            autoblocked = [ordered]@{ tipo = 'comando'; comando = $longRun; shell = 'auto'; autoIniciar = $true }
            logrotate = [ordered]@{ tipo = 'comando'; comando = 'echo rotation'; shell = 'auto' }
        }
    }
    $testCfg | ConvertTo-Json -Depth 8 | Set-Content -Path $cfgPath -Encoding UTF8

    function Invoke-CommandProfile([string]$Name) {
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $out = (& dotnet $dll up $Name 2>&1 | Out-String)
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldPreference
        }
        [pscustomobject]@{ Output = $out; ExitCode = $code }
    }

    $simple = Invoke-CommandProfile 'simple'
    if ($simple.ExitCode -ne 0 -or $simple.Output -notmatch 'stdout-ok' -or $simple.Output -notmatch 'stderr-ok') {
        throw "command stdout/stderr smoke failed: exit=$($simple.ExitCode) output=$($simple.Output)"
    }

    $session = Invoke-CommandProfile 'session'
    if ($session.ExitCode -ne 0 -or $session.Output -notmatch 'works') {
        throw "command same-shell-session smoke failed: exit=$($session.ExitCode) output=$($session.Output)"
    }

    $envResult = Invoke-CommandProfile 'env'
    if ($envResult.ExitCode -ne 0 -or $envResult.Output -notmatch '(?m)^inline\s*$' -or $envResult.Output -match 'from-file') {
        throw "command env precedence smoke failed: exit=$($envResult.ExitCode) output=$($envResult.Output)"
    }

    $fallback = Invoke-CommandProfile 'fallback'
    if ($fallback.ExitCode -ne 0 -or $fallback.Output -notmatch [regex]::Escape($work)) {
        throw "command working-directory fallback smoke failed: exit=$($fallback.ExitCode) output=$($fallback.Output)"
    }

    $cfgMissingGlobal = Get-Content $cfgPath -Raw | ConvertFrom-Json
    $cfgMissingGlobal.projetosDir = Join-Path $tmp 'missing-projects-dir'
    $cfgMissingGlobal | ConvertTo-Json -Depth 8 | Set-Content -Path $cfgPath -Encoding UTF8
    $homeFallback = Invoke-CommandProfile 'fallback'
    $expectedHome = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not $expectedHome) { $expectedHome = $HOME }
    if ($homeFallback.ExitCode -ne 0 -or $homeFallback.Output -notmatch [regex]::Escape($expectedHome)) {
        throw "command HOME fallback smoke failed: exit=$($homeFallback.ExitCode) output=$($homeFallback.Output)"
    }

    $fail = Invoke-CommandProfile 'failfast'
    if ($fail.ExitCode -eq 0 -or $fail.Output -notmatch 'before' -or $fail.Output -match '(?m)^after\s*$') {
        throw "command fail-fast smoke failed: exit=$($fail.ExitCode) output=$($fail.Output)"
    }

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
    $http.Timeout = [TimeSpan]::FromSeconds(10)
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

    function Invoke-ProfileAction([string]$Name, [string]$Action) {
        $response = $null
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$base/api/p/$Name/$Action")
        $request.Headers.Add('X-PIT', '1')
        $request.Content = [Net.Http.StringContent]::new('')
        try {
            $response = $http.SendAsync($request).GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ne 200) {
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "POST $Name/$Action returned $([int]$response.StatusCode): $body"
            }
        } finally {
            if ($response) { $response.Dispose() }
            $request.Dispose()
        }
    }

    function Wait-CommandState([string]$Name, [bool]$Running, [int]$Attempts = 40) {
        for ($i = 0; $i -lt $Attempts; $i++) {
            $statusJson = $http.GetStringAsync("$base/api/status").GetAwaiter().GetResult() | ConvertFrom-Json
            $item = @($statusJson | Where-Object nome -eq $Name) | Select-Object -First 1
            $isRunning = $null -ne $item -and ($null -ne $item.execucao -or $item.emUso -eq $true)
            if ($isRunning -eq $Running) { return $item }
            Start-Sleep -Milliseconds 250
        }
        throw "$Name did not reach running=$Running"
    }

    $autoBlocked = Wait-CommandState 'autoblocked' $false
    if ($null -ne $autoBlocked.execucao -or $autoBlocked.emUso -eq $true) {
        throw 'PIT_SEM_AUTOSTART=1 did not suppress automatic command startup'
    }

    Invoke-ProfileAction 'longrun' 'start'
    $beforeRestart = Wait-CommandState 'longrun' $true

    $doubleStart = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$base/api/p/longrun/start")
    $doubleStart.Headers.Add('X-PIT', '1')
    $doubleStart.Content = [Net.Http.StringContent]::new('')
    $doubleStartResponse = $http.SendAsync($doubleStart).GetAwaiter().GetResult()
    if ([int]$doubleStartResponse.StatusCode -ne 400) { throw "second command instance returned $([int]$doubleStartResponse.StatusCode), expected 400" }

    $beforeDesde = $beforeRestart.execucao.desde
    Invoke-ProfileAction 'longrun' 'reiniciar'
    $restarted = $false
    for ($i = 0; $i -lt 60; $i++) {
        $statusJson = $http.GetStringAsync("$base/api/status").GetAwaiter().GetResult() | ConvertFrom-Json
        $long = @($statusJson | Where-Object nome -eq 'longrun') | Select-Object -First 1
        if ($null -ne $long.execucao -and $long.execucao.desde -ne $beforeDesde -and $long.execucao.tipo -eq 'comando') {
            $restarted = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $restarted) { throw 'longrun restart did not create a new command execution' }
    Invoke-ProfileAction 'longrun' 'stop'
    $null = Wait-CommandState 'longrun' $false

    Invoke-ProfileAction 'timeout' 'start'
    $timeoutStatus = Wait-CommandState 'timeout' $false 60
    if ($null -eq $timeoutStatus.ultimo -or $timeoutStatus.ultimo.motivo -ne 'timeout') {
        throw "command timeout did not record motivo=timeout"
    }

    for ($n = 0; $n -lt 12; $n++) {
        Invoke-ProfileAction 'logrotate' 'start'
        $null = Wait-CommandState 'logrotate' $false
    }
    $logDir = Join-Path $tmp 'cache\logrotate\logs'
    $logs = @(Get-ChildItem $logDir -Filter '*.log' -File -ErrorAction Stop)
    if ($logs.Count -lt 1 -or $logs.Count -gt 10) { throw "command log rotation kept $($logs.Count) files, expected 1..10" }
    if (@($logs | Where-Object Length -gt (5MB)).Count -gt 0) { throw 'command log exceeded 5 MB' }
    $persisted = (& dotnet $dll logs logrotate 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $persisted -notmatch 'rotation') { throw "persisted command log smoke failed: $persisted" }

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

    # Não existe endpoint de execução arbitrária: só ações sobre comandos já persistidos em perfis.
    $arbitrary = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$base/api/p/longrun/exec")
    $arbitrary.Headers.Add('X-PIT', '1')
    $arbitrary.Content = [Net.Http.StringContent]::new('{"comando":"echo nao"}', [Text.Encoding]::UTF8, 'application/json')
    $arbitraryResponse = $http.SendAsync($arbitrary).GetAwaiter().GetResult()
    if ([int]$arbitraryResponse.StatusCode -ne 404) { throw "arbitrary command endpoint returned $([int]$arbitraryResponse.StatusCode), expected 404" }

    $invalidCommand = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Put, "$base/api/cfg")
    $invalidCommand.Headers.Add('X-PIT', '1')
    $invalidCommand.Content = [Net.Http.StringContent]::new('{"perfis":{"bad":{"tipo":"comando","comando":"   "}}}', [Text.Encoding]::UTF8, 'application/json')
    $invalidCommandResponse = $http.SendAsync($invalidCommand).GetAwaiter().GetResult()
    if ([int]$invalidCommandResponse.StatusCode -ne 400) { throw "invalid command profile returned $([int]$invalidCommandResponse.StatusCode), expected 400" }
    $cfgAfterInvalidCommand = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($cfgAfterInvalidCommand.perfis.PSObject.Properties.Name -contains 'bad') { throw 'invalid command profile was persisted' }

    $exit = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "$base/api/sair?rapido=1")
    $exit.Headers.Add('X-PIT', '1')
    $exit.Content = [Net.Http.StringContent]::new('')
    $exitResponse = $http.SendAsync($exit).GetAwaiter().GetResult()
    if ([int]$exitResponse.StatusCode -ne 200) { throw "POST /api/sair returned $([int]$exitResponse.StatusCode)" }

    if (-not $proc.WaitForExit(10000)) { throw 'pit ui did not exit after /api/sair' }
    if ($proc.ExitCode -ne 0) { throw "pit ui exited with code $($proc.ExitCode)" }

    Write-Host 'SMOKE OK: CLI, perfil Comando, fail-fast/env/fallback, restart/stop, API protections and clean shutdown.' -ForegroundColor Green
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
