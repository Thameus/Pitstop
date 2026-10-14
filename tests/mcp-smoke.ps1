$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repo 'src\Pitstop.Cli\bin\Release\net10.0\pit.dll'
if (-not (Test-Path $dll)) { throw "Build CLI first: $dll" }

$tmp = Join-Path $env:TEMP ('pitstop-mcp-test-' + [Guid]::NewGuid().ToString('N'))
$oldRoot = $env:PIT_RAIZ
$oldAuto = $env:PIT_SEM_AUTOSTART
$oldBrowser = $env:RUNNER_ABRIR_NAVEGADOR
$proc = $null

function Rpc([int]$id, [string]$method, [object]$p) {
    @{ jsonrpc = '2.0'; id = $id; method = $method; params = $p } | ConvertTo-Json -Depth 12 -Compress
}
try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Copy-Item -Recurse (Join-Path $repo 'web') (Join-Path $tmp 'web')
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    Set-Content -LiteralPath (Join-Path $tmp '.env') -Value "RUNNER_PORTA=$port" -Encoding Ascii
    $env:PIT_RAIZ = $tmp
    $env:PIT_SEM_AUTOSTART = '1'
    $env:RUNNER_ABRIR_NAVEGADOR = 'false'

    $argList = @('"' + $dll + '"', 'ui', "$port")
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList $argList -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $tmp 'server.stdout') -RedirectStandardError (Join-Path $tmp 'server.stderr')
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 150
        try {
            $null = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/status" -TimeoutSec 2
            $ready = $true
            break
        } catch { }
        if ($proc.HasExited) { break }
    }
    if (-not $ready) { throw 'Servidor isolado não iniciou' }

    $reqs = @(
        (Rpc 1 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'smoke'; version = '1' } }),
        (Rpc 2 'tools/list' @{}),
        (Rpc 3 'tools/call' @{ name = 'pitstop_status'; arguments = @{} }),
        (Rpc 4 'tools/call' @{ name = 'pitstop_save_profile'; arguments = @{ perfil = 'mcp-teste'; dados = @{ tipo = 'comando'; comando = 'echo ok'; shell = 'auto' } } }),
        (Rpc 9 'tools/call' @{ name = 'pitstop_save_profile'; arguments = @{ perfil = 'mcp-teste'; dados = @{ autoIniciar = $false } } }),
        (Rpc 10 'tools/call' @{ name = 'pitstop_profile'; arguments = @{ perfil = 'mcp-teste' } }),
        (Rpc 5 'tools/call' @{ name = 'pitstop_profiles'; arguments = @{} }),
        (Rpc 6 'tools/call' @{ name = 'pitstop_logs'; arguments = @{ perfil = 'mcp-teste'; linhas = 20 } }),
        (Rpc 7 'tools/call' @{ name = 'pitstop_delete_profile'; arguments = @{ perfil = 'mcp-teste' } }),
        (Rpc 8 'tools/call' @{ name = 'pitstop_profiles'; arguments = @{} })
    )
    $out = @($reqs | & dotnet $dll mcp)
    if ($LASTEXITCODE -ne 0) { throw "MCP exited $LASTEXITCODE" }
    if ($out.Count -ne $reqs.Count) { throw "Expected $($reqs.Count) messages, got $($out.Count): $out" }
    $r = @($out | ForEach-Object { $_ | ConvertFrom-Json })
    if ($r[0].result.serverInfo.name -ne 'pitstop') { throw 'initialize failed' }
    if ($r[1].result.tools.Count -lt 19) { throw 'Missing MCP tools' }
    if ($null -eq $r[1].result.tools[0].annotations.readOnlyHint) { throw 'Missing tool annotations' }
    foreach ($index in @(2,3,4,5,6,7,8,9)) {
        if ($r[$index].result.isError) { throw "Tool $index failed: $($r[$index].result.content[0].text)" }
    }
    $perfil = $r[5].result.content[0].text | ConvertFrom-Json
    if ($perfil.comando -ne 'echo ok') { throw 'Profile patch lost the existing command' }
    $lista = @($r[6].result.content[0].text | ConvertFrom-Json)
    if (-not ($lista | Where-Object { $_.nome -eq 'mcp-teste' })) { throw 'Profile not created' }
    $depois = @($r[9].result.content[0].text | ConvertFrom-Json)
    if ($depois | Where-Object { $_.nome -eq 'mcp-teste' }) { throw 'Profile not removed' }
    Write-Host 'OK MCP: initialize, tools/list, status, create/list/delete profile, logs (isolated)'
} finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
        $proc.WaitForExit()
    }
    $env:PIT_RAIZ = $oldRoot
    $env:PIT_SEM_AUTOSTART = $oldAuto
    $env:RUNNER_ABRIR_NAVEGADOR = $oldBrowser
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
