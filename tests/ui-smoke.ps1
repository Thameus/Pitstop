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
    $oldPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $html = (& $chrome '--headless=new' '--disable-gpu' '--no-first-run' '--no-default-browser-check' ('--user-data-dir='+$perfil) '--virtual-time-budget=3000' '--dump-dom' $url 2>$null | Out-String)
    } finally { $ErrorActionPreference = $oldPref }
    $pastas = ([regex]::Matches($html,'class="pasta-field"')).Count
    if ($pastas -lt 15) { throw "Seletores de pasta não renderizados: $pastas" }
    foreach($id in @('novoDetectar','pastaDlg','p_preparar','p_syncAuto','p_direto','p_reload','prontoUrl','buildMenu','syncMenu')) {
        if ($html -notmatch ('id="'+$id+'"')) { throw "Controle web ausente: $id" }
    }
    Write-Host "UI SMOKE OK: $pastas campos com seletor de pasta, cadastro, automação, modo direto e menu avançado."
} finally {
    if ($proc -and -not $proc.HasExited) {
        try { $proc.Kill($true); $proc.WaitForExit(3000) | Out-Null } catch {}
    }
    Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'dotnet.exe' -and $_.CommandLine -like ('*' + $temp + '*') } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }
    if(Test-Path $temp) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
}
