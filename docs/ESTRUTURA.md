# Estrutura do repositório

Este documento descreve a organização interna do Pitstop. O objetivo é manter a raiz simples e separar claramente código, scripts internos, testes, documentação e arquivos gerados.

## Árvore principal

```text
Pitstop/
├─ src/
│  ├─ Pitstop.Core/
│  ├─ Pitstop.App/
│  ├─ Pitstop.Cli/
│  └─ Pitstop.Setup.Windows/
├─ web/
├─ scripts/
│  ├─ install/
│  └─ release/
├─ tests/
├─ docs/
├─ third-party/
├─ .github/
├─ build.cmd
├─ build.sh
├─ empacotar.ps1
├─ instalar.cmd
├─ instalar.sh
├─ pit.cmd
├─ pit
├─ README.md
└─ LEIAME.md
```

## `src/`

### `Pitstop.Core`

Contém as regras compartilhadas por desktop, web e CLI:

- carregamento e validação de configuração;
- runner e ciclo de vida dos processos;
- Tomcat;
- Java/Maven;
- npm;
- pacote Java;
- perfil Comando;
- servidor HTTP local;
- abstrações de processo e diferenças de sistema operacional.

Código específico de Windows/Linux deve permanecer concentrado nas abstrações existentes, evitando espalhar condicionais pelo projeto.

### `Pitstop.App`

Aplicativo Avalonia:

- janela principal;
- bandeja;
- assistente;
- painéis dos perfis;
- notificações;
- autostart.

### `Pitstop.Cli`

CLI `pit` e modo `pit ui`.

### `Pitstop.Setup.Windows`

Instalador gráfico self-contained do Windows.

## `web/`

Interface web local e seus assets. A interface é servida pelo próprio Pitstop somente em loopback.

## `scripts/`

Scripts internos de manutenção.

### `scripts/install/`

Implementações reais de instalação/desinstalação:

- `instalar.ps1`;
- `desinstalar.ps1`;
- `ferramentas.ps1`;
- `instalar.sh`;
- `desinstalar.sh`.

No pacote final esses arquivos são copiados para a raiz para manter compatibilidade com instalações antigas.

### `scripts/release/`

- `empacotar.ps1` — empacotamento real;
- `pitstop-linux.run.sh` — stub do instalador Linux self-extracting.

A raiz mantém `empacotar.ps1` apenas como wrapper compatível.

## Entry points da raiz

A raiz deve conter somente comandos que alguém realmente executa diretamente:

- `build.cmd`;
- `build.sh`;
- `empacotar.ps1`;
- `instalar.cmd`;
- `instalar.sh`;
- `pit.cmd`;
- `pit`.

Scripts auxiliares não devem voltar para a raiz sem necessidade.

## `tests/`

- `smoke.ps1` — valida aplicação, API e perfil Comando;
- `smoke-package.ps1` — valida os pacotes de release no Windows;
- `smoke-package-linux.sh` — valida o pacote Linux self-contained.

## `docs/`

Documentação de manutenção, arquitetura de pastas e build/release.

## `third-party/`

Licenças, notices e versões das dependências redistribuídas. Não mover ou excluir arquivos legais sem revisar os testes de pacote.

## Dados locais e arquivos gerados

Não são versionados:

- `app/`;
- `dist/`;
- `cache/`;
- `logs/`;
- `bases/`;
- `config/perfis.json`;
- `config/perfis.json.bak`;
- `.env`;
- `Pitstop.lnk`;
- `src/**/bin/`;
- `src/**/obj/`.

Esses caminhos devem continuar cobertos pelo `.gitignore`.

## Regra para reorganizações futuras

Ao mover um arquivo interno, revisar sempre:

1. wrappers da raiz;
2. `.github/workflows/`;
3. scripts de empacotamento;
4. smoke tests;
5. caminhos usados pelo instalador;
6. README/LEIAME;
7. `.gitattributes` quando o tipo de arquivo mudar.
