# Pitstop

O Pitstop centraliza processos de desenvolvimento que normalmente ficam espalhados entre IDE, terminais e scripts. Ele roda localmente em Windows e Linux x64 e pode ser usado pela **janela + bandeja**, pela **interface web** ou pelo comando **`pit`**.

## Perfis disponíveis

Você pode criar perfis para:

- Tomcat com projetos Maven;
- WAR pronto;
- aplicação Java com `main`;
- pacote Java `.zip`/`.jar`;
- script npm;
- **Comando** personalizado.

O perfil **Comando** aceita várias linhas, executa tudo em sequência na mesma sessão de shell e para no primeiro erro. Também pode usar `.env`, variáveis extras, pasta de trabalho, URL, porta, mensagem de prontidão, timeout e início automático.

## Instalar

### Windows 10/11 x64

Baixe:

`pitstop-<versão>-setup-win-x64.exe`

Abra o instalador e escolha a pasta. Ele pode configurar atalhos/PATH e preparar JDK, Maven, Tomcat e Node.js de forma opcional.

Se o Windows mostrar SmartScreen, isso ocorre porque o executável ainda pode estar sem assinatura Authenticode.

### Linux x64

Baixe:

`pitstop-<versão>-linux-x64.run`

Execute:

```sh
chmod +x pitstop-*-linux-x64.run
./pitstop-*-linux-x64.run
```

Ou:

```sh
sh pitstop-*-linux-x64.run
```

O destino padrão é `~/.local/share/pitstop`.

Os pacotes `.tar.xz` também ficam disponíveis para uso portátil.

## Atualizar

Abra **Ajustes → Atualizações** e clique em **Buscar atualização**. A consulta só acontece quando você pedir; o Pitstop não mantém verificação em segundo plano.

Se existir uma versão nova, use **Baixar e atualizar**. O Pitstop:

1. baixa o pacote oficial do GitHub Release;
2. confere o SHA-256 do arquivo;
3. salva um backup pequeno de `.env` e `config/`;
4. para os perfis em execução após sua confirmação;
5. fecha, atualiza os arquivos do programa e abre novamente.

Os dados locais são preservados:

- `.env`;
- `config/`;
- `cache/`;
- `logs/`;
- `bases/`.

Os backups mínimos ficam em `cache/update-backups/` e o Pitstop mantém apenas os três mais recentes.

Também continua sendo possível baixar uma versão nova e instalá-la manualmente por cima da existente.

## Dependências

O .NET necessário ao Pitstop já está incluído no instalador.

Ferramentas adicionais só são necessárias para os perfis que as usam:

| Perfil | Dependência |
|---|---|
| Tomcat / WAR | JDK + Tomcat |
| Build Maven / app Java | Maven |
| Java / pacote Java | JDK/JRE |
| npm | Node.js |
| Comando | apenas o shell/comando usado |

## Iniciar rápido e trabalhar sem Sync manual

Na janela ou interface web, clique **+ → Detectar projeto pela pasta** para escolher um projeto Maven ou npm. Na web, use os ícones de pasta para navegar pelos diretórios locais em vez de copiar caminhos.

Novos perfis **Tomcat de projeto** têm opções de **Preparar ao iniciar** e **Sync automático**. Na primeira vez, o Pitstop prepara o projeto com o comando Maven configurado. Nas próximas, pode reaproveitar a preparação se entradas e saídas continuarem iguais. Um erro de build impede a subida; durante o build, **Parar** cancela a operação.

Enquanto o Tomcat estiver no ar, o Sync observa as fontes configuradas e sincroniza recursos sem botão manual. Classes Java precisam ser compiladas por Maven/IDE para aparecerem em `target/classes`. **Build completo** e **Sincronizar agora** estão no menu `…`. Perfis Tomcat antigos continuam no modo manual até você habilitar as opções na aba Artefatos.

Em opções avançadas há o modo **Publicação direta**, que utiliza as classes e arquivos da pasta de origem quando o Tomcat suportar. É experimental em projetos complexos: usa uma montagem WAR anterior para as bibliotecas e volta ao modo convencional se os requisitos não forem encontrados. A recarga automática do contexto é opcional; pode aumentar o consumo de CPU e reiniciar a aplicação ao mudar classes. A URL opcional de prontidão espera uma resposta HTTP local para marcar a aplicação como pronta.

## Primeiros passos

1. Abra o Pitstop.
2. Na primeira execução, configure caminhos padrão se quiser.
3. Clique em **+**.
4. Escolha o tipo do perfil.
5. Configure e salve.
6. Use **Iniciar**, **Parar**, **Reiniciar** e **Log** conforme o perfil.

No perfil Comando você também pode abrir um terminal diretamente na pasta configurada.

Fechar a janela não encerra os processos. Para encerrar o Pitstop de verdade, use **Parar e Sair**.

## Terminal

```text
pit status
pit up <perfil>
pit down <perfil>
pit logs <perfil>
pit build <perfil>
pit ui
pit ajustes
```

Ações como Build, Debug e Sync aparecem apenas em tipos que suportam essas operações.

## Arquivos do usuário

Na instalação ficam, entre outros:

- `.env` — ajustes globais;
- `config/perfis.json` — perfis;
- `cache/` — dados temporários e logs persistentes de perfis Comando;
- `logs/` — logs gerais;
- `bases/` — CATALINA_BASE de perfis Tomcat.

Esses dados não são publicados no repositório nem entram nos pacotes de Release.

Código e novas versões: https://github.com/Thameus/Pitstop

Licença: Apache 2.0. Consulte `LICENSE`, `NOTICE`, `LICENCAS.md` e `THIRD-PARTY-NOTICES.md`.
