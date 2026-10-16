param([int]$Porta = 18888)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repo 'src\Pitstop.Cli\bin\Release\net10.0\pit.dll'
if (!(Test-Path $dll)) { throw 'Compile primeiro: dotnet build src/Pitstop.slnx -c Release' }

$root = Join-Path $env:TEMP ('pitstop-issues-22-23-' + [Guid]::NewGuid().ToString('N'))
$a = Join-Path $root 'workspace-a\sistema'
$b = Join-Path $root 'workspace-b\api'
foreach ($path in @($a, $b)) {
    New-Item -ItemType Directory -Force (Join-Path $path 'target\app\WEB-INF') | Out-Null
    $nome = Split-Path $path -Leaf
    $pom = '<project><modelVersion>4.0.0</modelVersion><groupId>teste</groupId><artifactId>' + $nome + '</artifactId><version>1</version><packaging>war</packaging></project>'
    [IO.File]::WriteAllText((Join-Path $path 'pom.xml'), $pom)
    [IO.File]::WriteAllText((Join-Path $path 'target\app\index.html'), $nome)
}
Copy-Item -Recurse (Join-Path $repo 'web') (Join-Path $root 'web')
New-Item -ItemType Directory -Force (Join-Path $root 'config') | Out-Null

$cfg = @{
    projetosDir = (Split-Path $a -Parent)
    perfis = @{
        'Exemplo-22-23' = @{
            tipo = 'tomcat'; porta = 18080; portaShutdown = 18005; portaDebug = 15005
            projetos = @($a, $b)
            vmArgs = '-Xmx512m -Dvalidar-xxxxx=true'
            prepararAoIniciar = $false; syncAutomatico = $false
            artefatos = @(
                @{ ativo=$true; repo=$a; modulo=''; contexto='/sistema'; build='mvn package'; sync=@() }
                @{ ativo=$true; repo=$b; modulo=''; contexto='/api'; build='mvn package'; sync=@() }
            )
        }
    }
}
[IO.File]::WriteAllText((Join-Path $root 'config\perfis.json'), ($cfg | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
$runtime = Join-Path $root 'runtime'
Copy-Item -Recurse (Split-Path $dll -Parent) $runtime
$dll = Join-Path $runtime 'pit.dll'
$env:PIT_RAIZ = $root
$env:PIT_SEM_AUTOSTART = '1'
$env:RUNNER_ABRIR_NAVEGADOR = 'false'
Write-Host 'Cenario isolado criado em:' $root
Write-Host 'Projeto sistema:' $a
Write-Host 'Projeto API:' $b
Write-Host 'Abra no navegador: http://127.0.0.1:'$Porta'/'
Write-Host 'Somente teste de interface. Para executar Tomcat real, configure JDK e Tomcat no perfil.'
Write-Host 'Ctrl+C encerra o runner de teste; a instalacao original nao e modificada.'
& dotnet $dll ui $Porta
