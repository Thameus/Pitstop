$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repo 'src\Pitstop.Cli\bin\Release\net10.0\pit.dll'
if (-not (Test-Path $dll)) { throw 'Build Pitstop.Cli -c Release first' }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('pitstop-external-status-' + [Guid]::NewGuid().ToString('N'))
$oldRoot = $env:PIT_RAIZ
$oldAuto = $env:PIT_SEM_AUTOSTART
$oldBrowser = $env:RUNNER_ABRIR_NAVEGADOR
$ui = $null
$external = $null
$portListener = $null
function FreePort() {
    $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $l.Start()
    $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port
    $l.Stop()
    return $p
}
try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Copy-Item -Recurse (Join-Path $repo 'web') (Join-Path $tmp 'web')
    $portUi = FreePort
    $portProfile = FreePort
    while ($portProfile -eq $portUi) { $portProfile = FreePort }
    $env:PIT_RAIZ = $tmp
    $env:PIT_SEM_AUTOSTART = '1'
    $env:RUNNER_ABRIR_NAVEGADOR = 'false'
    $runningWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    $longCmd = if ($runningWindows) { 'ping -t 127.0.0.1 >nul' } else { 'sleep 60' }
    $cfg = @{
        perfis = @{
            externalCmd = @{ tipo = 'comando'; comando = $longCmd; shell = 'auto' }
            tomcatExt = @{ tipo = 'tomcat'; porta = $portProfile }
            npmExt = @{ tipo = 'npm'; porta = $portProfile; pasta = $tmp; script = 'dev' }
        }
    }
    $cfgDir = Join-Path $tmp 'config'
    New-Item -ItemType Directory -Force $cfgDir | Out-Null
    $cfg | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $cfgDir 'perfis.json') -Encoding UTF8
    $ui = [Diagnostics.Process]::new()
    $ui.StartInfo.FileName = (Get-Command dotnet).Source
    $ui.StartInfo.Arguments = '"' + $dll + '" ui ' + $portUi
    $ui.StartInfo.UseShellExecute = $false
    $ui.StartInfo.CreateNoWindow = $true
    $ui.StartInfo.Environment['PIT_RAIZ'] = $tmp
    $ui.StartInfo.Environment['PIT_SEM_AUTOSTART'] = '1'
    $ui.StartInfo.Environment['RUNNER_ABRIR_NAVEGADOR'] = 'false'
    if (-not $ui.Start()) { throw 'Could not launch isolated Pitstop UI' }
    $base = "http://127.0.0.1:$portUi"
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        if ($ui.HasExited) { throw 'Isolated UI exited too early' }
        try {
            $req = Invoke-WebRequest -UseBasicParsing "$base/api/status" -TimeoutSec 2
            if ($req.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 150
    }
    if (-not $ready) { throw 'Isolated UI not ready' }
    $external = [Diagnostics.Process]::new()
    $external.StartInfo.FileName = (Get-Command dotnet).Source
    $external.StartInfo.Arguments = '"' + $dll + '" up externalCmd'
    $external.StartInfo.UseShellExecute = $false
    $external.StartInfo.CreateNoWindow = $true
    $external.StartInfo.Environment['PIT_RAIZ'] = $tmp
    if (-not $external.Start()) { throw 'Cannot start external CLI' }
    $pidFile = Join-Path $tmp 'cache\externalCmd\pit.pid'
    $pidReady = $false
    for ($i=0; $i -lt 60; $i++) {
        if (Test-Path $pidFile) { $pidReady=$true; break }
        if ($external.HasExited) { throw "CLI exited: $($external.ExitCode)" }
        Start-Sleep -Milliseconds 150
    }
    if (-not $pidReady) { throw 'CLI did not write pit.pid' }
    function GetStatus([string]$name) {
        $items = Invoke-RestMethod "$base/api/status" -TimeoutSec 5
        return $items | Where-Object { $_.nome -eq $name } | Select-Object -First 1
    }
    $seen = GetStatus 'externalCmd'
    if (-not $seen.emUso -or $seen.execucao.tipo -ne 'externo' -or $seen.execucao.pid -le 0 -or $seen.execucao.pronto -eq $null) {
        throw "CLI ownership invisible to UI: $($seen | ConvertTo-Json -Compress -Depth 5)"
    }
    Write-Output 'OK CLI process is recognized by the running UI as external owned profile'

    $externalPidRecord = Get-Content -Raw -LiteralPath $pidFile
    $tomcatBase = Join-Path $tmp 'bases\tomcatExt'
    New-Item -ItemType Directory -Force $tomcatBase | Out-Null
    Set-Content -LiteralPath (Join-Path $tomcatBase 'pit.pid') -Value $externalPidRecord -Encoding Ascii
    $starting = GetStatus 'tomcatExt'
    if (-not $starting.emUso -or $starting.execucao.tipo -ne 'externo' -or $null -ne $starting.execucao.pronto) {
        throw 'External Tomcat must be shown as starting before its configured port opens'
    }
    Write-Output 'OK external Tomcat is shown as starting before the port opens'
    Remove-Item -LiteralPath (Join-Path $tomcatBase 'pit.pid') -Force
    $portListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $portProfile)
    $portListener.Start()
    $unknown = GetStatus 'tomcatExt'
    if (-not $unknown.emUso -or $null -ne $unknown.execucao) {
        throw ('Unknown port owner was incorrectly adopted: ' + ($unknown | ConvertTo-Json -Compress -Depth 6))
    }
    Set-Content -LiteralPath (Join-Path $tomcatBase 'pit.pid') -Value $externalPidRecord -Encoding Ascii
    $owned = GetStatus 'tomcatExt'
    if (-not $owned.emUso -or $owned.execucao.tipo -ne 'externo' -or $null -eq $owned.execucao.pronto) {
        throw "Known external Tomcat PID not recognized: $($owned | ConvertTo-Json -Compress -Depth 5)"
    }
    Write-Output 'OK known external PID is recognized; unknown occupied port stays unknown'
    $npmBase = Join-Path $tmp 'cache\npmExt'
    New-Item -ItemType Directory -Force $npmBase | Out-Null
    Set-Content -LiteralPath (Join-Path $npmBase 'pit.pid') -Value $externalPidRecord -Encoding Ascii
    $npmStatus = GetStatus 'npmExt'
    if (-not $npmStatus.emUso -or $npmStatus.execucao.tipo -ne 'externo' -or $null -eq $npmStatus.execucao.pronto) {
        throw 'An npm profile started in a separate Pitstop CLI must be recognized'
    }
    Remove-Item -LiteralPath (Join-Path $npmBase 'pit.pid') -Force
    Write-Output 'OK npm profiles use the same external ownership signal'
    $reg = $externalPidRecord.Trim().Split('|')
    if ($reg.Count -ge 2 -and $reg[1] -match '^\d+$') {
        $invalidStamp = $reg[0] + '|' + ([long]$reg[1] + 1)
        Set-Content -LiteralPath (Join-Path $tomcatBase 'pit.pid') -Value $invalidStamp -Encoding Ascii
        $stale = GetStatus 'tomcatExt'
        if ($null -ne $stale.execucao) { throw 'Stale/recycled PID was incorrectly adopted' }
    }
    Write-Output 'OK stale PID timestamp cannot claim ownership'
    Remove-Item -LiteralPath (Join-Path $tomcatBase 'pit.pid') -Force
    $unknownAgain = GetStatus 'tomcatExt'
    if ($null -ne $unknownAgain.execucao) { throw 'External status remains after PID record removed' }
    $portListener.Stop()
    $portListener = $null

    # A API do Pitstop pode parar o perfil que foi iniciado pelo terminal.
    Invoke-RestMethod -Uri "$base/api/p/externalCmd/stop" -Method Post -Headers @{ 'X-PIT'='1' } -ContentType 'application/json' -Body '{}' | Out-Null
    $stopped = $false
    for ($i=0; $i -lt 60; $i++) {
        $state = GetStatus 'externalCmd'
        if (-not $state.emUso -and $null -eq $state.execucao) { $stopped=$true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $stopped) { throw 'Externally launched CLI process still appears running after Stop' }
    Write-Output 'OK Stop action ends externally launched profile and clears status'
} finally {
    if ($portListener) { $portListener.Stop() }
    foreach ($pr in @($external, $ui)) {
        if ($null -ne $pr) {
            try {
                if (-not $pr.HasExited) {
                    try { $pr.Kill($true) } catch { $pr.Kill() }
                    $pr.WaitForExit(5000) | Out-Null
                }
                $pr.Dispose()
            } catch { }
        }
    }
    $env:PIT_RAIZ=$oldRoot
    $env:PIT_SEM_AUTOSTART=$oldAuto
    $env:RUNNER_ABRIR_NAVEGADOR=$oldBrowser
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
