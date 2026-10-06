param(
    [string[]]$Rids = @('win-x64', 'linux-x64'),
    [switch]$Assinar,
    [string]$Certificado = $env:PITSTOP_CERT_SHA1,
    [string]$Timestamp = 'http://timestamp.sectigo.com',
    [string]$Signtool = ''
)

# Wrapper de compatibilidade. A implementação fica em scripts\release\empacotar.ps1.
$impl = Join-Path $PSScriptRoot 'scripts\release\empacotar.ps1'
& $impl -Rids $Rids -Assinar:$Assinar -Certificado $Certificado -Timestamp $Timestamp -Signtool $Signtool
