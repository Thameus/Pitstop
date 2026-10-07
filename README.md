# Pitstop

Pitstop é um **runner local de ambientes de desenvolvimento** para Windows e Linux x64. Ele centraliza o que normalmente ficaria espalhado em Run Configurations da IDE, scripts e terminais, com uma interface única para iniciar, parar, reiniciar, acompanhar logs e abrir os serviços.

O mesmo núcleo pode ser usado de três formas: **aplicativo desktop + bandeja**, **interface web local** (`pit ui`) e **CLI** (`pit`).

## O que o Pitstop executa

O Pitstop trabalha com seis tipos de perfil:

- **Tomcat + projetos Maven**: wars exploded, build, debug e sync de arquivos/classes;
- **WAR pronto**: executa um release já gerado, sem recompilar;
- **Aplicação Java**: classe `main` de um módulo Maven, com classpath calculado automaticamente;
- **Pacote Java**: `.zip` ou `.jar` pronto;
- **npm**: `npm run <script>`, com Node opcional por perfil;
- **Comando**: comandos genéricos e multilinha, executados em sequência no mesmo shell.

### Perfil Comando

O perfil **Comando** serve para qualquer processo que não precise de um executor especializado. Ele suporta:

- vários comandos, uma linha por etapa;
- execução sequencial na mesma sessão de shell;
- interrupção no primeiro erro (*fail-fast*);
- shell `auto`, `cmd`, PowerShell/`pwsh`, `sh`, `bash` ou interpretador personalizado;
- `.env` opcional e variáveis adicionais do perfil;
- diretório de trabalho opcional;
- porta, URL, mensagem de prontidão e timeout opcionais;
- início automático;
- abrir terminal na pasta do perfil;
- logs persistentes rotativos: até 10 arquivos de 5 MB por perfil;
- parada graciosa por 5 segundos e, se necessário, encerramento forçado da árvore de processos.

A API HTTP nunca recebe um comando arbitrário para executar: ela só aciona **perfis persistidos** na configuração.

## Instalação

Arquivos recomendados em cada GitHub Release:

| Sistema | Instalador recomendado | Portátil |
|---|---|---|
| Windows 10/11 x64 | `pitstop-<versão>-setup-win-x64.exe` | `pitstop-<versão>-win-x64.zip` |
| Linux x64 | `pitstop-<versão>-linux-x64.run` | `pitstop-<versão>-linux-x64.tar.xz` |

O runtime .NET necessário ao Pitstop já vai embutido nos pacotes de distribuição.

Nos pacotes Windows self-contained, a distribuição do .NET usa licenciamento misto: arquivos normalmente cobertos por MIT convivem com binários específicos cobertos pela Microsoft .NET Library License. O pacote inclui o mapeamento oficial e os termos aplicáveis em `third-party/dotnet/`; detalhes em [LICENCAS.md](LICENCAS.md).

JDK, Maven, Tomcat e Node.js são opcionais e só são necessários quando o tipo de perfil usa essas ferramentas. O instalador pode preparar cópias portáteis ou você pode apontar instalações já existentes.

Reinstalar por cima mantém os dados do usuário: `.env`, `config/`, `cache/`, `logs/` e `bases/`.

## Atualizações pelo aplicativo

Em **Ajustes → Atualizações**, o Pitstop pode consultar manualmente a última versão estável publicada no GitHub Releases. Não existe verificação periódica nem serviço de atualização em segundo plano.

Quando existe uma versão nova, o fluxo é:

1. baixar o pacote oficial adequado ao sistema;
2. validar o SHA-256 publicado pelo GitHub;
3. criar um backup pequeno de `.env` e dos arquivos de `config/`;
4. pedir confirmação se houver perfis em execução;
5. fechar o Pitstop de forma graciosa;
6. substituir somente os arquivos do programa;
7. reabrir a versão nova.

No Windows, o aplicativo baixa o ZIP portátil e copia o helper integrado `app/updater/Pitstop.Updater.exe` para `%TEMP%`. Esse helper roda fora da instalação, valida novamente o SHA-256, usa lock exclusivo por instalação, espera o processo antigo terminar, verifica outros `Pitstop.exe`/`pit.exe` ainda usando a mesma raiz e faz retry de locks transitórios antes de trocar `app/`, `web/` e `third-party/`. Cada troca usa diretórios temporários e restaura a versão anterior se a ativação falhar. O Setup continua sendo usado para instalação/reinstalação. No Linux, o pacote `.run` oficial é executado depois que o processo atual termina.

Os backups mínimos ficam em `cache/update-backups/`; somente os três mais recentes são mantidos.

Guia curto para usuário final: [LEIAME.md](LEIAME.md).

## Desenvolvimento local

Requisito: **.NET 10 SDK**, conforme `global.json`.

Windows:

```powershell
.\build.cmd
```

Linux:

```sh
sh ./build.sh
```

Os dois publicam o aplicativo e a CLI em `app/`.

Smoke principal:

```powershell
pwsh ./tests/smoke.ps1
```

No Windows PowerShell 5.1 também é possível usar:

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1
```

## Empacotamento e Release

O comando compatível da raiz continua sendo:

```powershell
powershell -ExecutionPolicy Bypass -File .\empacotar.ps1
```

Esse arquivo é apenas um wrapper. A implementação fica em:

`scripts/release/empacotar.ps1`

Ela gera em `dist/`:

- instalador Windows `.exe`;
- instalador Linux self-extracting `.run`;
- pacote portátil Windows `.zip` e pacote portátil Linux `.tar.xz`;
- `SHA256SUMS.txt`.

O GitHub Release é disparado por uma tag `v<versão>`. A tag precisa ser igual à versão de `src/Directory.Build.props`.

Fluxo recomendado:

1. alterar `<Version>`;
2. abrir PR e esperar CI/CodeQL;
3. mergear na `main`;
4. criar a tag no commit da `main`;
5. `git push origin v<versão>`;
6. o workflow de Release empacota, testa os arquivos finais e publica a Release.

Detalhes: [docs/BUILD-RELEASE.md](docs/BUILD-RELEASE.md).

## Estrutura do repositório

```text
Pitstop/
├─ src/
│  ├─ Pitstop.Core/           regras, perfis, processos e servidor local
│  ├─ Pitstop.App/            app Avalonia + bandeja
│  ├─ Pitstop.Cli/            CLI pit
│  └─ Pitstop.Setup.Windows/  instalador gráfico Windows
├─ web/                       interface web e recursos visuais
├─ scripts/
│  ├─ install/                implementação de instalar/desinstalar/ferramentas
│  └─ release/                empacotamento e stub do instalador Linux
├─ tests/                     smoke tests do código e dos pacotes
├─ docs/                      documentação de manutenção
├─ third-party/               licenças/notices das dependências redistribuídas
├─ .github/                   CI, CodeQL, Release e Dependabot
├─ build.cmd / build.sh       entrypoints de desenvolvimento
├─ empacotar.ps1              wrapper compatível para release
├─ instalar.cmd / instalar.sh wrappers para instalação a partir do repositório
├─ pit.cmd / pit              wrappers da CLI
├─ README.md                  documentação técnica e de desenvolvimento
└─ LEIAME.md                  guia curto do usuário final
```

Diretórios gerados ou locais, não versionados:

`app/`, `dist/`, `cache/`, `logs/`, `bases/`, `config/perfis.json`, `.env` e `Pitstop.lnk`.

Mais detalhes: [docs/ESTRUTURA.md](docs/ESTRUTURA.md).

## Configuração

### Global: `.env`

O arquivo global aceita, entre outras:

| Chave | Função |
|---|---|
| `RUNNER_PORTA` | porta da interface web/API, padrão 9999 |
| `PROJETOS_DIR` | pasta global de projetos |
| `TOMCAT_HOME` | Tomcat padrão |
| `JDK_HOME` | JDK padrão |
| `MAVEN_HOME` | Maven padrão |
| `NODE_HOME` | Node padrão |
| `BASES_DIR` | CATALINA_BASE dos perfis Tomcat |
| `PACOTES_DIR` | pasta padrão de releases |
| `STOP_TIMEOUT_SEG` | timeout geral de parada dos perfis tradicionais |
| `RUNNER_ABRIR_NAVEGADOR` | abrir navegador ao executar `pit ui` |

O arquivo `.env` real é local e ignorado pelo Git. O repositório contém apenas `.env.exemplo`.

### Perfis: `config/perfis.json`

A janela e a interface web gravam os perfis nesse arquivo. Ele também é local e ignorado pelo Git.

Exemplo de perfil Comando:

```json
{
  "tipo": "comando",
  "comando": "echo preparando\necho pronto",
  "shell": "auto",
  "pasta": "",
  "envArquivo": "",
  "env": "MINHA_VARIAVEL=valor",
  "porta": 0,
  "url": "",
  "prontoLog": "",
  "autoIniciar": false
}
```

Quando `pasta` está vazia, o runner usa `PROJETOS_DIR` se ele estiver configurado **e existir**; caso contrário, usa a HOME do usuário.

## CLI

Exemplos:

```text
pit status
pit up <perfil>
pit down <perfil>
pit build <perfil>
pit logs <perfil>
pit ui
pit ajustes
```

As ações disponíveis dependem do tipo de perfil. Um perfil Comando não expõe Build, Debug ou Sync.

## Segurança

- servidor HTTP somente em `127.0.0.1`;
- validação de `Host` contra DNS rebinding;
- operações mutáveis exigem `X-PIT: 1`;
- limite de corpo nas requisições;
- nomes de perfil são validados antes de persistir;
- a API não possui endpoint de execução arbitrária de comandos;
- arquivos privados/locais são excluídos dos pacotes;
- smoke de release verifica hashes, privacidade, notices, dependências e executáveis empacotados;
- CodeQL roda nos PRs.

## Documentação

- [LEIAME.md](LEIAME.md) — instalação e uso rápido;
- [DESIGN.md](DESIGN.md) — sistema visual e baseline de UI desktop/web;
- [docs/ESTRUTURA.md](docs/ESTRUTURA.md) — organização do repositório;
- [docs/BUILD-RELEASE.md](docs/BUILD-RELEASE.md) — build, CI, empacotamento, tags e release;
- [LICENCAS.md](LICENCAS.md) — resumo das licenças redistribuídas;
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — notices de terceiros.

## Licença

Apache 2.0. Consulte `LICENSE` e `NOTICE`.

Código-fonte e releases: https://github.com/Thameus/Pitstop
