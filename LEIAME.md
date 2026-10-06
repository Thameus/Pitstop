# Pitstop

Sobe e para, com um clique, o que você rodaria numa Run Configuration da IDE: **Tomcat** com os wars dos seus projetos
Maven (war exploded, com build e sync), **aplicações Java** com `main`, **pacotes prontos** (.war, .zip, .jar) e
**scripts npm**. Janela + ícone na bandeja, tela web (`pit ui`) e linha de comando (`pit`). Windows e Linux, 64 bits.

## Instalar

**Windows 10/11** — baixe `pitstop-<versão>-setup-win-x64.exe` e dê duplo clique. O assistente pergunta onde
instalar, se quer atalhos/PATH, detecta ferramentas já existentes e pode baixar JDK 21, Maven, Tomcat e Node.js
portáteis. Se você escolher apenas a raiz de uma unidade (por exemplo `D:\`), o destino vira `D:\Pitstop` para
não espalhar arquivos do programa na raiz do disco. O .NET necessário ao Pitstop já está embutido, então não precisa instalar runtime separado. Não exige
administrador por padrão. Se aparecer "O Windows protegeu o computador" (o Pitstop ainda não tem assinatura digital),
clique em *Mais informações → Executar assim mesmo*.

**Linux** — baixe `pitstop-<versão>-linux-x64.run` e execute:

```sh
chmod +x pitstop-*-linux-x64.run
./pitstop-*-linux-x64.run
```

Também funciona com `sh pitstop-*-linux-x64.run`. O instalador pergunta o destino, detecta ferramentas e oferece
downloads opcionais. Os `.tar.xz` permanecem disponíveis como pacotes portáteis.
Instala em `~/.local/share/pitstop`, cria os comandos `pitstop` e `pit` em `~/.local/bin` e o atalho no menu de
aplicativos. Desinstalar: `sh ~/.local/share/pitstop/desinstalar.sh`. No GNOME o ícone da bandeja precisa da extensão
*AppIndicator*; sem ela o Pitstop abre pela janela normalmente.

Atualizar = instalar a versão nova por cima: perfis e ajustes (`.env`, `config/`) ficam.

## O que você precisa (só o que for usar)

O .NET já vem dentro do Pitstop. JDK, Tomcat, Maven e Node são opcionais: o instalador pode baixá-los para a
pasta `tools` do Pitstop ou você pode apontar os que já possui. Todos são gratuitos:

| Para | Precisa | Onde baixar |
|---|---|---|
| Tomcat, aplicações Java, Maven | JDK | https://adoptium.net (Eclipse Temurin) |
| perfis Tomcat e war | Apache Tomcat (zip/tar.gz descompactado) | https://tomcat.apache.org |
| build e classpath dos projetos | Apache Maven | https://maven.apache.org |
| perfis npm | Node.js (LTS) | https://nodejs.org |

## Primeiros passos

1. Abra o Pitstop: na primeira vez um assistente pergunta os caminhos padrão (tudo opcional).
2. Clique em **+** (ou num dos cartões) e escolha o tipo do perfil; informe nome e caminhos.
3. Tomcat: marque os projetos e os módulos war na aba *Artefatos* → **Build** → **Iniciar**.
4. **Depurar** abre a porta de debug (conecte a IDE com *Remote JVM Debug*). **Sync** copia JSP/JS/CSS e classes para
   o Tomcat no ar.

Fechar a janela não para nada: o Pitstop continua na bandeja. Para sair de vez: **Parar e Sair** (bandeja ou menu ⋯).

## Terminal

```
pit status                       perfis e estado
pit up <perfil> [--debug]        sobe no terminal (Ctrl+C para)
pit down <perfil>                para
pit build <perfil>               build
pit ui                           a mesma tela no navegador (http://localhost:9999)
pit ajustes                      onde ficam os arquivos e os ajustes atuais
```

## Arquivos

Tudo fica na pasta de instalação: `.env` (ajustes), `config/perfis.json` (perfis), `cache/`, `logs/` e `bases/`
(o CATALINA_BASE de cada perfil Tomcat).

Código-fonte, novas versões e problemas: https://github.com/Thameus/Pitstop

Licença: Apache 2.0 — Copyright 2026 Matheus Silva (`LICENSE`, `NOTICE`). Terceiros: `LICENCAS.md`, `THIRD-PARTY-NOTICES.md` e `third-party/`.
