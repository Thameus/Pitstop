$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$fonte = Join-Path $repo 'src\Pitstop.Cli\bin\Release\net10.0'
if (-not (Test-Path (Join-Path $fonte 'pit.dll'))) { throw 'Faça build da CLI antes do teste' }
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path $chrome)) { Write-Host 'UI SKIP: Chrome indisponível'; exit 0 }
$temp = Join-Path $env:TEMP ('pitstop-ui-smoke-'+[guid]::NewGuid().ToString('N'))
$proc = $null
try {
    New-Item -ItemType Directory -Force $temp | Out-Null
    Copy-Item -Recurse (Join-Path $repo 'web') (Join-Path $temp 'web')
    Copy-Item -Recurse $fonte (Join-Path $temp 'runtime')
    $dll = Join-Path $temp 'runtime\pit.dll'
    # Config isolada (PIT_RAIZ=$temp) com um perfil de teste: sem perfil a lista nunca renderiza.
    New-Item -ItemType Directory -Force (Join-Path $temp 'config') | Out-Null
    '{"perfis":{"SmokeTeste":{"tipo":"comando","comando":"echo smoke","autoIniciar":false}}}' | Set-Content -Encoding utf8 (Join-Path $temp 'config\perfis.json')
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $porta = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = (Get-Command dotnet).Source
    $psi.Arguments = '"' + $dll + '" ui ' + $porta
    $psi.CreateNoWindow = $true
    $psi.UseShellExecute = $false
    $psi.Environment['PIT_RAIZ'] = $temp
    $psi.Environment['PIT_SEM_AUTOSTART'] = '1'
    $psi.Environment['RUNNER_ABRIR_NAVEGADOR'] = 'false'
    $proc = [Diagnostics.Process]::Start($psi)
    $url = "http://127.0.0.1:$porta/"
    $pronto = $false
    for($i=0;$i -lt 40;$i++) {
        try {
            $r = Invoke-WebRequest ($url + 'api/status') -UseBasicParsing -TimeoutSec 2
            if($r.StatusCode -eq 200) { $pronto = $true; break }
        } catch {}
        Start-Sleep -Milliseconds 250
    }
    if(-not $pronto) { throw 'Runner local não iniciou' }
    $perfil = Join-Path $temp 'chrome-profile'
    # Com perfil selecionado a tela abre um EventSource (log) que nunca fecha: --dump-dom não termina.
    # Por isso o Chrome é dirigido via DevTools (CDP): navega, espera, lê o DOM e coleta erros de JS.
    $portaCdp = 0
    $l2 = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l2.Start()
    $portaCdp = ([Net.IPEndPoint]$l2.LocalEndpoint).Port; $l2.Stop()
    $chromeProc = Start-Process $chrome -ArgumentList '--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',"--user-data-dir=$perfil","--remote-debugging-port=$portaCdp",'about:blank' -PassThru -WindowStyle Hidden
    $ws = [Net.WebSockets.ClientWebSocket]::new()
    try {
        $wsUrl = $null
        for($i=0;$i -lt 40 -and -not $wsUrl;$i++) {
            try { $wsUrl = @((Invoke-RestMethod "http://127.0.0.1:$portaCdp/json/list" -TimeoutSec 2) | ForEach-Object { if($_.type -eq 'page') { $_.webSocketDebuggerUrl } })[0] } catch { Start-Sleep -Milliseconds 250 }
        }
        if(-not $wsUrl) { throw 'Chrome (DevTools) não iniciou' }
        $ws.ConnectAsync([Uri]$wsUrl, [Threading.CancellationToken]::None).Wait()
        $script:cdpId = 0
        $errosJs = [Collections.Generic.List[string]]::new()
        function Receber($ms) {
            $cts = [Threading.CancellationTokenSource]::new($ms)
            $buf = New-Object byte[] 65536
            $sb = [Text.StringBuilder]::new()
            try {
                do {
                    $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), $cts.Token).GetAwaiter().GetResult()
                    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
                } until ($r.EndOfMessage)
            } catch { return $null }
            return ($sb.ToString() | ConvertFrom-Json)
        }
        function Cdp($metodo, $params) {
            $script:cdpId++
            $id = $script:cdpId
            $msg = (@{ id = $id; method = $metodo; params = $params } | ConvertTo-Json -Depth 6 -Compress)
            $bytes = [Text.Encoding]::UTF8.GetBytes($msg)
            $ws.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None).Wait()
            while($true) {
                $m = Receber 15000
                if($null -eq $m) { throw "CDP sem resposta a $metodo" }
                if($m.method -eq 'Runtime.exceptionThrown') { $errosJs.Add([string]$m.params.exceptionDetails.exception.description) }
                elseif($m.method -eq 'Runtime.consoleAPICalled' -and $m.params.type -eq 'error') { $errosJs.Add(($m.params.args | ForEach-Object { $_.value }) -join ' ') }
                if($m.id -eq $id) { return $m }
            }
        }
        [void](Cdp 'Runtime.enable' @{})
        [void](Cdp 'Page.enable' @{})
        [void](Cdp 'Page.navigate' @{ url = $url })
        Start-Sleep -Seconds 4
        $dom = Cdp 'Runtime.evaluate' @{ expression = 'document.documentElement.outerHTML'; returnByValue = $true }
        $html = [string]$dom.result.result.value
        $tx = Cdp 'Runtime.evaluate' @{ expression = "document.querySelector('#perfis').innerText + ' | ' + document.querySelector('#pillTxt').innerText"; returnByValue = $true }
        $textoUi = [string]$tx.result.result.value
        # drena eventos pendentes (erros de JS que chegaram depois do evaluate)
        while($null -ne ($m = Receber 300)) {
            if($m.method -eq 'Runtime.exceptionThrown') { $errosJs.Add([string]$m.params.exceptionDetails.exception.description) }
        }
    } finally {
        try { $ws.Dispose() } catch {}
        if($chromeProc -and -not $chromeProc.HasExited) { try { $chromeProc.Kill($true) } catch { $chromeProc.Kill() } }
    }
    $pastas = ([regex]::Matches($html,'class="pasta-field"')).Count
    if ($pastas -lt 15) { throw "Seletores de pasta não renderizados: $pastas" }
    foreach($id in @('novoDetectar','pastaDlg','p_preparar','p_syncAuto','p_direto','p_reload','prontoUrl','buildMenu','syncMenu','novoProjetoDir','adicionarProjeto','vmArgs')) {
        if ($html -notmatch ('id="'+$id+'"')) { throw "Controle web ausente: $id" }
    }
    if ($html -notmatch 'class="perfil"') { throw 'Lista de perfis não renderizou (perfil SmokeTeste ausente)' }
    if ($html -notmatch 'SmokeTeste') { throw 'Perfil SmokeTeste não aparece na tela' }
    if ($textoUi -match 'RAM') { throw "Perfil parado não deve mostrar RAM: $textoUi" }
    if ($errosJs.Count -gt 0) { throw ('Erro de JS na tela: ' + $errosJs[0]) }
    Write-Host "UI SMOKE OK: $pastas campos com seletor de pasta, cadastro, automação, modo direto e menu avançado."
} finally {
    if ($proc -and -not $proc.HasExited) {
        try {
            try { $proc.Kill($true) } catch { $proc.Kill() }
            $proc.WaitForExit(3000) | Out-Null
        } catch {}
    }
    Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -like ('*' + $temp + '*') } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }
    if(Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
}
