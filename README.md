# Pitstop

Runner local, para **Windows e Linux (x64)**, do que você rodaria numa Run Configuration da IDE:

- **Tomcat** com os wars dos seus projetos Maven em modo *exploded* (como o Tomcat Local do IntelliJ ou o WTP do
  Eclipse): um `CATALINA_BASE` por perfil, build, debug (JPDA) e **sync** de estáticos/classes sem rebuild;
- **Tomcat com .war pronto** de um release (sem compilar);
- **aplicação Java com `main`** de um módulo Maven (classpath do Maven, compila só o que mudou);
- **pacote Java pronto** (`.zip` extraído ou `.jar`, `java -jar`);
- **script npm** (`npm run <script>`: dev server do Angular, Vite, React...).

Três jeitos de usar, sobre o mesmo núcleo: **janela + ícone na bandeja** (`Pitstop`), **tela web**
(`pit ui` → `http://localhost:9999`) e **linha de comando** (`pit`). O .NET vai embutido. JDK, Tomcat, Maven
e Node são opcionais: o instalador pode baixar cópias portáteis oficiais ou você pode apontar instalações existentes.

> Código aberto: https://github.com/Thameus/Pitstop · Downloads: https://github.com/Thameus/Pitstop/releases · Licença: Apache 2.0 (`LICENSE`, `NOTICE`).
> Usuário final: o guia curto de instalação é o `LEIAME.md`, incluído também nos pacotes de release.

---

## 1. Instalar

Arquivos recomendados em cada release:

| | Windows 10/11 x64 | Linux x64 |
|---|---|---|
| instalador | `pitstop-<versão>-setup-win-x64.exe` | `pitstop-<versão>-linux-x64.run` |
| executar | duplo clique no `.exe` | `chmod +x <arquivo>.run && ./<arquivo>.run` (ou `sh <arquivo>.run`) |
| destino padrão | `%LOCALAPPDATA%\Programs\Pitstop` (raiz `D:\` vira `D:\Pitstop`) | `~/.local/share/pitstop` |
| assistente | pasta, atalhos/PATH, JDK, Maven, Tomcat, Node.js e pasta de projetos | pasta, detecção/download de JDK, Maven, Tomcat e Node.js, pasta de projetos |
| desinstalar | *Aplicativos instalados* ou `desinstalar.ps1` | `sh ~/.local/share/pitstop/desinstalar.sh` |

Os `.tar.xz` continuam publicados como **pacotes portáteis** para quem prefere extrair e rodar `instalar.cmd` /
`instalar.sh` manualmente. `SHA256SUMS.txt` cobre instaladores e pacotes portáteis.

Sem administrador/root. Reinstalar por cima atualiza e **mantém** `.env`, `config/`, `cache/`, `logs/` e `bases/`.
Linux: a janela precisa de `libX11`, `fontconfig`, `libICE` e `libSM` (o instalador avisa se faltar); a bandeja usa
StatusNotifierItem (KDE, XFCE, Cinnamon...; no GNOME, extensão *AppIndicator*). Sem bandeja, a janela continua
sendo a porta de entrada e o menu ⋯ tem *Parar e sair*.

### Primeira execução

Sem `.env`, abre o **assistente**: boas-vindas (o que precisa e onde baixar) → caminhos padrão (JDK, Tomcat, Maven,
Node e pasta de projetos; já preenche o que achar em `JAVA_HOME`, `MAVEN_HOME`, `CATALINA_HOME`, `NODE_HOME`) → preferências (iniciar
com o sistema, porta, pastas) → pronto. Tudo opcional: *Pular* grava um `.env` vazio. Depois, os mesmos campos ficam
em **Ajustes** (⚙ na lateral, menu ⋯, menu da bandeja, ou o diálogo Ajustes da tela web).

Criar perfil pede nome **e os caminhos do tipo** (com 📁 para escolher e validação). O nome aceita até 64
letras/números e `-`, `.`, `_`, sem espaço/separador de caminho e sem ponto no início/fim. Tomcat → Tomcat + JDK +
pasta de projetos; app Java → módulo Maven + Java; pacote → arquivo + Java; npm → pasta do `package.json` + Node.
Vazio = o padrão de Ajustes.

## 2. Dependências opcionais

O instalador pode baixar essas ferramentas diretamente dos distribuidores oficiais para `<Pitstop>/tools`,
ou você pode apontar uma instalação existente. Nada disso é necessário se você não usa o respectivo tipo de perfil.

| Perfil | Precisa | Gratuito em |
|---|---|---|
| Tomcat, war | JDK compatível (Tomcat 9: Java 8+; 10.1: 11+; 11: 17+) e o Tomcat descompactado | https://adoptium.net · https://tomcat.apache.org |
| Tomcat (build), app Java | Apache Maven (ou `mvn` no PATH) | https://maven.apache.org |
| app Java, pacote | JDK/JRE | https://adoptium.net |
| npm | Node.js (pasta da distribuição ou `node` no PATH) | https://nodejs.org |

## 3. Estrutura da pasta

```
Pitstop/
├─ src/                      código (.NET 10)
│  ├─ Pitstop.Core/          regras: config, perfis, processos, Tomcat, Maven, npm, pacotes, servidor HTTP
│  │   └─ So.cs              tudo que muda entre Windows e Linux (shell, scripts, PATH, encerrar processo)
│  ├─ Pitstop.App/           Pitstop(.exe): janela + bandeja + servidor da tela web (Avalonia)
│  ├─ Pitstop.Cli/           pit(.exe): linha de comando e "pit ui"
│  ├─ Directory.Build.props  versão e alvo (net10.0)
│  └─ nuget.config           fonte dos pacotes (nuget.org)
├─ web/                      tela web (ui.html — marca a raiz do Pitstop), ícones, fontes
├─ tests/                    smoke do build e dos pacotes finais
├─ third-party/              licenças/notices upstream e VERSIONS.json das dependências redistribuídas
├─ config/perfis.json        perfis (dado do usuário; criado vazio na 1ª execução)
├─ .env                      ajustes globais (dado do usuário; o assistente cria a partir do .env.exemplo)
├─ build.cmd / build.sh      desenvolvimento: publica em app/
├─ empacotar.ps1             distribuição: gera .exe/.run e pacotes portáteis .tar.xz
├─ installer/                stub do instalador self-extracting Linux (.run)
├─ instalar.cmd/.ps1/.sh     instaladores internos/portáteis
├─ src/Pitstop.Setup.Windows instalador gráfico self-contained do Windows
├─ desinstalar.ps1/.sh       desinstaladores (vão no pacote)
├─ pit.cmd / pit             atalhos da linha de comando
├─ .github/                  CI, release e Dependabot
├─ .gitattributes            finais de linha e binários para Git
├─ LEIAME.md                 guia curto para quem instala o Pitstop
├─ LICENSE · NOTICE          licença do Pitstop (Apache 2.0, Copyright 2026 Matheus Silva)
├─ THIRD-PARTY-NOTICES.md    índice dos avisos de terceiros
└─ LICENCAS.md               resumo das licenças redistribuídas
gerados (podem ser apagados): app/, cache/, logs/, bases/, dist/, Pitstop.lnk
```

A raiz é a pasta que tem `web/ui.html`; os executáveis em `app/` (ou `src/.../bin/`) sobem até achá-la.
`PIT_RAIZ` no ambiente força outra raiz (testes); `PIT_SEM_AUTOSTART=1` impede ler/gravar o "iniciar com o sistema".

## 4. Compilar e empacotar

Precisa do **.NET 10 SDK**.

- **Desenvolvimento**: `build.cmd` (Windows; feche o Pitstop antes, o Windows trava o .exe) ou `sh build.sh`
  (Linux). Saída em `app/` (framework-dependent).
- **Distribuição**: `powershell -ExecutionPolicy Bypass -File empacotar.ps1 [-Rids win-x64,linux-x64]` (no
  Windows). Publica self-contained para cada RID (Linux com `InvariantGlobalization`, sem dependência de `libicu`),
  monta o payload (app, web, instaladores internos, LEIAME, licenças/notices, `.env.exemplo`), gera
  `pitstop-<versão>-setup-win-x64.exe`, `pitstop-<versão>-linux-x64.run` e mantém os dois `.tar.xz` portáteis.
  O bit de execução dos arquivos do Linux vai num manifesto mtree (o NTFS não guarda). Assinatura (desligada até ter
  certificado): `-Assinar -Certificado <SHA1>` assina `Pitstop.exe`, `pit.exe` e o Setup com `signtool` e
  carimbo de tempo. **Nada desta máquina entra**: `.env`, `config/`, `cache/`, `logs/`, `bases/` ficam de fora.

### 4.1 Smoke tests

Depois do build: `pwsh ./tests/smoke.ps1`. Ele usa uma raiz temporária e valida CLI, criação da configuração vazia,
servidor HTTP local, bloqueio de Host inválido, exigência de `X-PIT` e encerramento limpo.

Depois de empacotar: `pwsh ./tests/smoke-package.ps1`. Ele confere SHA-256, ausência de dados/arquivos privados,
notices obrigatórios, permissões executáveis do Linux, versões das dependências, execução do `pit.exe`, payload do
Setup e uma instalação real isolada do `.exe` em pasta temporária. O workflow de Release ainda executa o `.run`
e o pacote Linux no Ubuntu antes de publicar.

### 4.2 Publicar uma versão

1. Subir a versão em `src/Directory.Build.props` (`<Version>`) e commitar.
2. `git tag v<versão>` e `git push origin v<versão>`.
3. O workflow `release.yml` (runner Windows Server 2025) roda o `empacotar.ps1`, valida Windows/Linux e cria o
   Release com `.exe`, `.run`, os dois `.tar.xz` e `SHA256SUMS.txt`. Ele recusa tag diferente da versão do
   `Directory.Build.props`.

Contribuições: issues e pull requests no GitHub; o `ci.yml` compila em Windows e Linux a cada PR. Código novo segue
o estilo do existente (nomes e comentários em português) e não escreve nada específico de sistema fora do `So.cs`.

## 5. Configuração

### 5.1 `.env` — global

`CHAVE=valor` (`#` comenta). Manda sobre o bloco global do `perfis.json`; cada perfil ainda pode informar o seu.
Editado pelo assistente/Ajustes (`Ajustes.cs` preserva comentários e ordem) ou à mão.

| Chave | Uso | Padrão |
|---|---|---|
| `RUNNER_PORTA` | porta da tela web e da API | `9999` |
| `PROJETOS_DIR` | onde procurar repositórios Maven (perfis Tomcat) | — |
| `TOMCAT_HOME` | Tomcat dos perfis que não informam outro | — |
| `JDK_HOME` | JAVA_HOME do Tomcat, do Maven e dos apps | `JAVA_HOME` do sistema |
| `MAVEN_HOME` | Maven (pasta com `bin/mvn`) | `mvn` do PATH |
| `BASES_DIR` | onde ficam os `CATALINA_BASE` (`<dir>/<perfil>`) | `<Pitstop>/bases` |
| `PACOTES_DIR` | pasta de releases (uma subpasta por versão com o .war/.zip/.jar) | — |
| `MAVEN_ARGS` | argumentos sugeridos para artefato novo | `-B -ntp package -DskipTests` |
| `MAVEN_OFFLINE` | `-o` no classpath/compilação do app Java | `false` |
| `STOP_TIMEOUT_SEG` | espera do stop gracioso antes de matar | `20` |
| `LOG_MAX_LINHAS` / `LOG_REPLAY_LINHAS` | log em memória por perfil / linhas enviadas ao abrir a tela web | `5000` / `1500` |
| `PERFIS_ARQUIVO` | arquivo de perfis | `config/perfis.json` |
| `RUNNER_ABRIR_NAVEGADOR` | `pit ui` abre o navegador | `true` |

### 5.2 `config/perfis.json` — por perfil

Editado pela janela/tela web. Exemplo com os cinco tipos (caminhos ilustrativos):

```jsonc
{
  "perfis": {
    "meu-tomcat": {                          // tipo omitido = Tomcat com wars exploded
      "tomcatHome": "/opt/tomcat-10.1",      // vazio = TOMCAT_HOME
      "javaHome": "/opt/jdk-21",             // vazio = JDK_HOME / JAVA_HOME
      "mavenHome": "/opt/maven",             // vazio = MAVEN_HOME / mvn do PATH
      "porta": 8080, "portaDebug": 5005,     // shutdown = porta − 75 se omitido; portaJmx/portaAjp opcionais
      "vmArgs": "-Xmx1g",
      "projetosDir": "/home/eu/projetos",
      "projetos": ["/home/eu/projetos/loja"],
      "artefatos": [{
        "ativo": true, "repo": "/home/eu/projetos/loja", "modulo": "loja-web", "contexto": "/loja",
        "build": "mvn -B -ntp package -DskipTests -pl loja-web -am",
        "sync": [{ "de": "src/main/webapp", "para": "" }, { "de": "target/classes", "para": "WEB-INF/classes" }]
      }]
    },
    "app-desktop": { "tipo": "java", "modulo": "/home/eu/projetos/loja/loja-desktop", "mainClass": "com.exemplo.App",
                     "javaHome": "/opt/jdk-21", "portaDebug": 5006, "compilarAntes": true, "prontoLog": "" },
    "release":     { "tipo": "war", "porta": 8090, "artefatos": [{ "war": "/srv/releases/v1.2.0/loja.war" }] },
    "ferramenta":  { "tipo": "zip", "pacote": "/srv/releases/v1.2.0/ferramenta.zip", "jar": "", "trabalho": "" },
    "front":       { "tipo": "npm", "pasta": "/home/eu/projetos/front", "script": "start", "porta": 4200,
                     "nodeHome": "/opt/node-24", "build": "npm run build", "env": "NODE_OPTIONS=--max-old-space-size=4096" }
  }
}
```

Caminhos relativos dentro do perfil (sync, jar, trabalho) aceitam `/` nos dois sistemas (`\` gravado no Windows é
convertido). Ao gravar, as chaves globais que vieram do `.env` não são copiadas para o JSON; `.bak` a cada gravação.

## 6. Como funciona

```
                 ┌──────────── Pitstop (.NET 10, Avalonia) ───────────┐
 navegador ──►   │ Kestrel 127.0.0.1:<porta>        (Servidor.cs)     │
 (web/ui.html)   │   /api/cfg, /api/status, /api/ajustes, /api/p/...  │
     ▲  SSE log  │ Runner.cs ◄── janela e bandeja (chamada direta)    │
     └───────────│ Tomcat.PrepararBase ─► bases/<perfil>/conf/...     │
                 │ shell ─► catalina.bat|sh run / jpda run            │──► Tomcat (JVM) por perfil
                 │ shell ─► mvn ... (build)                           │──► target/<war explodido>
                 │ Sync ─► copia estáticos / classes                  │
                 │ java ─► -cp classpath.jar <main> | -jar <pacote>   │──► app Java
                 │ shell ─► npm run <script> -- <args>                │──► dev server
                 └────────────────────────────────────────────────────┘
 pit ──► mesmo Pitstop.Core, com a saída no terminal
 shell = cmd.exe /d /s /c (Windows) · /bin/sh -c (Linux)
```

**Diferenças por sistema** (`So.cs`): `catalina.bat`/`catalina.sh` (o bit de execução é corrigido se o Tomcat veio de
um zip), `npm.cmd`/`npm`, `mvn.cmd`/`mvn`, `java.exe`/`java`, separador `;`/`:` no PATH e no classpath, `pathExtra`
também no `LD_LIBRARY_PATH` no Linux. **Encerrar**: Windows `taskkill /T` (sem `/F` = `WM_CLOSE`, com `/F` = força);
Linux SIGTERM/SIGKILL na árvore lida de `/proc`. **Órfãos**: no Windows um Job Object derruba os filhos se o Pitstop
morrer de qualquer jeito; no Linux a saída do Pitstop (Parar e Sair, Ctrl+C, SIGTERM, SIGHUP) para os perfis antes.
**`pit.pid`**: `pid|início` do processo (Windows: hora de início; Linux: `starttime` de `/proc/<pid>/stat`, estável
entre leituras) — pid reciclado não engana o stop.

### 6.1 Tomcat

1. **Base** (`Tomcat.PrepararBase`): cria `<BASES_DIR>/<perfil>/{conf,logs,temp,work,webapps}`, copia o `conf` de
   origem **sem sobrescrever** (o app pode gravar nos `.properties` dele), **regenera o `server.xml`** com as portas
   do perfil (HTTP, shutdown; AJP só se pedido, em 127.0.0.1) e grava um `conf/Catalina/localhost/<ctx>.xml` por
   artefato com o `docBase` na pasta explodida em `target/` (a mais nova com `WEB-INF`, ou a fixa do perfil).
2. **Subir**: checa as portas (HTTP, shutdown, debug, JMX, AJP) com connect em 127.0.0.1 — pega IntelliJ ou outro
   perfil no ar; `CATALINA_HOME/BASE`, `JAVA_HOME`, `CATALINA_OPTS` (+ JMX) e, em debug, `JPDA_*`; "pronto" = linha
   `Server startup in`.
3. **Parar**: `catalina stop` (só se o Tomcat é do Pitstop: processo ou `pit.pid` vivo — nunca manda SHUTDOWN para
   o Tomcat da IDE); após `STOP_TIMEOUT_SEG`, mata a árvore.
4. **Build**: o comando de cada artefato ativo, em sequência, com o Maven do perfil (`mvn` do começo é trocado pelo
   `mavenHome`); recusado com o perfil no ar. Log filtrado (progresso, erros, resumo).
5. **Sync**: copia só o que é mais novo que o destino (estáticos → raiz, `target/classes` → `WEB-INF/classes`).
   Não é espelho.
6. **Descoberta** (`Projetos.cs`, `Pom.cs`): repositórios com `pom.xml` em `PROJETOS_DIR`, `<module>` até 2 níveis;
   `packaging=war` vira candidato (contexto = `wtpContextName` > `finalName` > `artifactId`).

### 6.2 Aplicação Java

Classpath por `mvn dependency:build-classpath` (cacheado até um `pom.xml` mudar), com os módulos do próprio
repositório trocados pelo `target/classes` deles; compila só os módulos com fonte mais novo que as classes;
**jar de classpath** (só `MANIFEST` com `Class-Path`) porque a lista passa do limite da linha de comando do Windows.
Sobe `java <vmArgs> [-agentlib:jdwp…] -cp classpath.jar <main> <args>`. Parar = pedir para fechar
(`WM_CLOSE`/SIGTERM), forçar após o tempo limite.

### 6.3 Pacotes prontos (war e zip)

Extração uma vez por arquivo em `cache/<perfil>/…-<chave>` (chave = caminho + data + tamanho; versão nova = pasta
nova, a velha é apagada; extração em `.tmp` renomeada no fim). zip: acha o jar com `Main-Class` fora de `lib/` (ou o
informado), copia `conf` sem sobrescrever e sobe `java -jar`. **Build** = extrair de novo. **Versões**: o seletor
lista o mesmo arquivo nas pastas irmãs (`…/v1.2/app.war`, `…/v1.3/app.war`); vazio = `PACOTES_DIR`.

### 6.4 npm

Valida `package.json`, script, `node_modules` e `node` do `nodeHome` (raiz do zip do Windows ou `bin/` do tar do
Linux); sobe `npm run <script> -- <args>` pelo shell, `nodeHome` na frente do PATH, `NO_COLOR`, `BROWSER=none`.
"Pronto" = regex `prontoLog` (vazio = `Compiled successfully`, `ready in`, `Local: http`). Parar mata a árvore
(shell → npm → node). Sem Depurar nem Sync.

## 7. API HTTP

Só em `127.0.0.1` (`Servidor.cs`). `Host` precisa ser `localhost:<porta>`/`127.0.0.1:<porta>` (DNS rebinding);
método que não é GET exige `X-PIT: 1`; corpo até 1 MB; erro → `400 { "erro": "..." }`.

| Método | Rota | Ação |
|---|---|---|
| GET | `/` · `/icone.svg` · `/favicon.ico` · `/fonts/*.woff2` | tela e recursos |
| GET / PUT | `/api/cfg` | perfis + globais + `_versao` / grava (recusa se mudou por fora) |
| GET / PUT | `/api/ajustes` | campos, valores, sugestões do sistema, `primeiraVez`, `so` / grava o `.env` |
| GET | `/api/status` | estado de todos os perfis |
| GET | `/api/tomcats` · `/api/projetos[?dir=]` · `/api/mains?modulo=` · `/api/scripts?pasta=` · `/api/versoes?arq=&ext=` | seletores da tela |
| GET | `/api/p/<perfil>/log` | SSE do log |
| POST | `/api/p/<perfil>/start` · `debug` · `stop` · `reiniciar` · `build` · `sync` · `preparar` · `abrir` · `limpar` | ações |
| POST | `/api/p/<perfil>/renomear` `{ "para": "<nome>" }` | renomeia (parado) |
| POST | `/api/sair[?rapido=1]` · `/api/tela` | para tudo e sai · mostra a janela (2ª instância) |

## 8. Linha de comando

```
pit ui [porta]                         tela web (fechar/Ctrl+C para tudo o que ela subiu)
pit up <perfil> [--debug] [--build]    sobe no terminal atual (Ctrl+C para)
pit up <app> [--sem-compilar]          app Java: não compila antes
pit down <perfil>                      mesmo Stop da janela
pit build <perfil> · pit sync <perfil>
pit status · pit projetos · pit ajustes
```

## 9. Limitações e cuidados

- **Build com o perfil no ar é recusado** (jars em uso), inclusive o subido por fora. Stop durante um build
  cancela o Maven e pode deixar `target/` parcial.
- **Linux**: se o Pitstop for morto com SIGKILL, os filhos continuam (não existe Job Object); `pit.pid` permite
  parar depois pela janela/`pit down`.
- `MAVEN_OFFLINE=true` exige o `.m2` populado.
- `conf` da base **não é sobrescrito** após a primeira cópia (exceto `server.xml`).
- **JMX** ligado é sem autenticação e sem SSL (só 127.0.0.1).
- Log em memória: reiniciar o Pitstop perde o histórico (os logs do Tomcat ficam em `bases/<perfil>/logs/`).
- npm: o campo porta não muda a porta do dev server (passe `--port N` nos argumentos também).
- Renomear só com o perfil parado.
