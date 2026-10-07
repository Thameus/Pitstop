# Build, CI e Release

## Desenvolvimento

Requisito: .NET 10 SDK definido em `global.json`.

### Windows

```powershell
.\build.cmd
```

O script procura `dotnet` no PATH e também em `%ProgramFiles%\dotnet\dotnet.exe`.

Saída: `app/`.

### Linux

```sh
sh ./build.sh
```

Saída: `app/`.

## Smoke do código

```powershell
pwsh ./tests/smoke.ps1
```

O smoke usa uma raiz temporária e não deve alterar a configuração real do usuário. Entre outras coisas ele valida:

- CLI;
- servidor HTTP;
- proteções da API;
- ciclo de vida dos perfis;
- perfil Comando;
- fail-fast;
- mesma sessão de shell;
- `.env` + env inline;
- fallback da pasta;
- timeout;
- autostart bloqueado em teste;
- restart/stop;
- rotação de logs.

O smoke dos pacotes executa o helper integrado `Pitstop.Updater.exe` e também o modo legado `--smoke-update` do Setup. Ele valida troca de arquivos, detecção de bloqueadores e preservação de `.env` e `config/perfis.json`.

## Empacotamento

Entry point compatível:

```powershell
powershell -ExecutionPolicy Bypass -File .\empacotar.ps1
```

Implementação:

`scripts/release/empacotar.ps1`

O empacotador:

1. publica App e CLI self-contained para cada RID;
2. monta uma área `dist/stage`;
3. copia apenas arquivos permitidos;
4. achata os scripts de `scripts/install/` para a raiz do pacote;
5. inclui `web/`, notices e licenças;
6. gera o `.zip` portátil do Windows e o `.tar.xz` portátil do Linux;
7. gera o instalador Windows;
8. gera o `.run` Linux;
9. grava `SHA256SUMS.txt`.

Arquivos locais como `.env`, `config/`, `cache/`, `logs/` e `bases/` nunca entram no pacote.

## Teste dos pacotes

Depois de empacotar:

```powershell
pwsh ./tests/smoke-package.ps1
```

O workflow também executa:

```sh
sh ./tests/smoke-package-linux.sh dist
```

em Ubuntu usando exatamente o pacote produzido. Esse smoke também simula uma atualização via `.run --update` e confirma que `.env` e `config/perfis.json` continuam intactos.

O smoke Windows também atua como barreira de licenciamento: valida os arquivos de notice obrigatórios, confere as bibliotecas do `Pitstop.deps.json` contra o mapeamento revisado e inspeciona os binários do runtime Windows. A presença de `coreclr.dll`/`Microsoft.DiaSymReader.Native.*` exige os termos da Microsoft .NET Library; se aparecer um binário conhecido com outra licença sem o respectivo notice, a release é interrompida.

## CI

`.github/workflows/ci.yml` roda em:

- Windows Server 2025;
- Ubuntu 24.04.

Ele executa:

1. checkout;
2. setup do .NET;
3. build Release;
4. validação sintática dos scripts shell;
5. smoke principal.

CodeQL roda separadamente.

## Atualização pelo aplicativo

A janela consulta `https://api.github.com/repos/Thameus/Pitstop/releases/latest` somente quando o usuário clica em **Buscar atualização**. Não há polling nem serviço residente.

Para a atualização automática funcionar, a Release precisa conter o asset exato da plataforma:

- Windows x64: `pitstop-<versão>-win-x64.zip`;
- Linux x64: `pitstop-<versão>-linux-x64.run`.

O app exige o digest SHA-256 informado pelo GitHub para o asset. Depois do download e da validação:

1. salva `.env`, `config/perfis.json` e `config/perfis.json.bak` quando existirem em `cache/update-backups/`;
2. mantém somente os três backups mais recentes;
3. confirma a parada se houver perfis rodando;
4. inicia um atualizador externo e encerra o Pitstop graciosamente.

No Windows, o pacote inclui `app/updater/Pitstop.Updater.exe`. O aplicativo copia esse helper para a mesma pasta temporária do download e o inicia com:

```text
--package <zip> --sha256 <digest> --destination <raiz> --parent-pid <pid> --restart --cleanup <pasta-temporaria>
```

O helper roda fora da instalação, valida novamente o SHA-256 do ZIP, usa mutex por instalação, extrai e valida a estrutura do pacote antes da troca, espera o PID antigo terminar e verifica se outro `Pitstop.exe`/`pit.exe` da mesma raiz continua aberto. A troca de `app/`, `web/` e `third-party/` usa `.update-new`/`.update-old`, com retry curto para locks transitórios e rollback se a ativação falhar. O novo Pitstop é reiniciado com `WorkingDirectory` em `<raiz>\app`; em caso de sucesso o diretório temporário de download/helper é removido depois que o updater termina.

O `Pitstop-Setup.exe --update` permanece como compatibilidade/fallback e para reinstalação manual, mas não é usado pelo botão normal de atualização do aplicativo.

No Linux, um script temporário espera o PID do Pitstop terminar e executa o `.run` oficial em modo `--update <destino>`. Esse modo não abre o assistente de ferramentas: ele apenas reaplica os arquivos do programa preservando os dados locais e devolve o controle para o script, que reabre o aplicativo somente em caso de sucesso.

## Publicar uma versão

A versão oficial fica em:

`src/Directory.Build.props`

Exemplo:

```xml
<Version>3.0.6</Version>
```

Fluxo:

1. alterar a versão em branch;
2. abrir PR;
3. esperar CI + CodeQL;
4. mergear;
5. atualizar a `main` local;
6. criar tag anotada:
   ```powershell
   git tag -a v3.0.6 -m "Pitstop 3.0.6"
   git push origin v3.0.6
   ```
7. acompanhar o workflow Release.

O workflow rejeita uma tag que não corresponda ao `<Version>`.

## Workflow de Release

`.github/workflows/release.yml` é disparado por tags `v*`.

Etapas:

1. validar tag e suporte a xz;
2. executar `scripts/release/empacotar.ps1`;
3. rodar smoke dos arquivos gerados;
4. subir o release candidate como artifact;
5. baixar o artifact em Ubuntu;
6. executar o pacote Linux;
7. publicar a GitHub Release.

## Artefatos esperados

```text
pitstop-<versão>-setup-win-x64.exe
pitstop-<versão>-win-x64.zip
pitstop-<versão>-linux-x64.run
pitstop-<versão>-linux-x64.tar.xz
SHA256SUMS.txt
```

## Assinatura Windows

O empacotador mantém suporte opcional a Authenticode:

```powershell
.\empacotar.ps1 -Assinar -Certificado <SHA1>
```

Também pode usar `PITSTOP_CERT_SHA1` no ambiente. Nenhum certificado ou segredo deve ser salvo no repositório.
