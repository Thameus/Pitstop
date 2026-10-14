# Pitstop — Design System e Baseline de UI

Este documento registra o padrão visual oficial do Pitstop e serve como referência para a revisão de interface iniciada na linha 3.0.x.

O objetivo não é redesenhar o produto. A identidade atual é preservada: interface escura/clara, roxo como acento, sidebar fixa, cards discretos, tipografia Space Grotesk e JetBrains Mono e foco em produtividade para desenvolvimento local.

## 1. Princípios

1. **Clareza antes de decoração** — o estado do perfil e a ação principal devem ser compreendidos em poucos segundos.
2. **Consistência entre interfaces** — desktop e web podem ter implementações diferentes, mas devem usar a mesma linguagem visual e os mesmos conceitos.
3. **Ação principal evidente** — Iniciar/Parar domina visualmente; ações de suporte não competem com ela.
4. **Progressive disclosure** — opções avançadas existem, mas não precisam estar expostas o tempo todo.
5. **Teclado primeiro** — qualquer ação disponível pelo mouse deve ser alcançável por teclado quando tecnicamente aplicável.
6. **Estados explícitos** — Parado, Iniciando, No ar, Depurando, Build, Reiniciando, Executando externamente, Porta ocupada e Erro não devem parecer equivalentes.
7. **Texto curto e operacional** — rótulos explicam o que fazer; tooltips explicam o que significa.
8. **Sem duplicação visual desnecessária** — evitar múltiplas superfícies com o mesmo peso visual quando hierarquia resolve.
9. **Desktop e web compartilham tokens** — divergências devem ser intencionais e documentadas.
10. **Mudanças visuais devem preservar funcionalidade** — nenhuma simplificação deve esconder uma ação importante.

## 2. Baseline atual

Baseline técnico da revisão:

- versão: **3.0.6**;
- janela desktop padrão: **1320 × 860**;
- janela mínima: **980 × 620**;
- sidebar desktop: **280 px**;
- cabeçalho da marca: **76 px**;
- rodapé da sidebar: **64 px**;
- captura de referência usada na auditoria: **1336 × 899**, Windows, tema escuro, perfis fictícios;
- UI web: breakpoint principal em **960 px**;
- build Release no baseline: **0 erros / 0 warnings**.

A captura usada para inspeção foi gerada com uma raiz temporária e perfis fictícios, sem carregar os perfis reais do usuário. Ela não é versionada para evitar churn binário desnecessário; o baseline oficial é descrito neste documento.

## 3. Identidade visual

### 3.1 Tipografia

**Sans / interface**
- Space Grotesk
- uso: títulos, labels, botões, descrições, menus, tabs, estados.

**Mono / dados técnicos**
- JetBrains Mono
- uso: portas, URLs, paths, logs, comandos, argumentos, valores técnicos.

Regra:
- não usar monoespaçada apenas por estética;
- usar mono quando o valor representa algo que o usuário pode copiar, digitar ou comparar tecnicamente.

### 3.2 Cores — Light

| Token | Valor | Uso |
|---|---:|---|
| Bg | `#F7F6FA` | fundo geral |
| Lado | `#FFFFFF` | sidebar |
| Surf | `#FFFFFF` | cards/inputs/botões |
| Surf2 | `#FBFAFD` | superfície secundária |
| Line | `#E3DFEA` | bordas/divisores |
| Fg | `#1A1622` | texto principal |
| Mut | `#5F596B` | texto secundário |
| Off | `#A7A1B2` | estado inativo |
| Acc | `#7338B3` | acento |
| AccHover | `#5F2C96` | acento hover |
| AccTxt | `#7338B3` | texto de ação |
| Ok | `#1E8452` | sucesso/no ar |
| Dbg | `#2F63C9` | depuração |
| Warn | `#9A6206` | atenção/ocupado |
| Err | `#C4302B` | erro/perigo |

### 3.3 Cores — Dark

| Token | Valor | Uso |
|---|---:|---|
| Bg | `#0E0D12` | fundo geral |
| Lado | `#131118` | sidebar |
| Surf | `#18161E` | cards/inputs/botões |
| Surf2 | `#1D1A24` | superfície secundária |
| Line | `#2B2735` | bordas/divisores |
| Fg | `#EDEAF3` | texto principal |
| Mut | `#A09AAD` | texto secundário |
| Off | `#6B6577` | estado inativo |
| Acc | `#7338B3` | acento |
| AccHover | `#8547C8` | acento hover |
| AccTxt | `#B48AE6` | texto de ação |
| Ok | `#43C07F` | sucesso/no ar |
| Dbg | `#6EA8FE` | depuração |
| Warn | `#E3A63F` | atenção/ocupado |
| Err | `#F0625E` | erro/perigo |

O roxo `#7338B3` é o acento principal oficial. Não criar novas cores de marca sem necessidade funcional.


## 4. Escala de espaçamento e geometria

A escala oficial deve convergir para múltiplos de 4 px.

| Token lógico | Valor | Uso típico |
|---|---:|---|
| xs | 4 | microgap |
| sm | 8 | ícone-texto, ações próximas |
| md | 12 | conteúdo interno compacto |
| lg | 16 | grids e blocos |
| xl | 24 | separação entre seções |
| 2xl | 32 | diálogos/assistente |
| 3xl | 40 | margens da área principal desktop |

### Radius

- controles: **10 px**;
- cards de dados: **12 px**;
- superfícies grandes/log/dialog: **14 px**;
- pill/status: **999 px / totalmente arredondado**.

### Alturas oficiais alvo

A auditoria encontrou diferenças pequenas entre desktop e web. O padrão de convergência será:

| Componente | Alvo | Desktop atual | Web atual |
|---|---:|---:|---:|
| botão padrão | **44 px** | 40 | 44 |
| botão ícone | **44 × 44** | 40 × 40 | 44 × 44 |
| input padrão | **44 px** | 42 | 44 |
| botão compacto | **36 px** | 32–34 em alguns pontos | 36 |
| tab | **48 px** | equivalente visual | 48 |
| status pill | **28 px** | 28 | 28 |

Exceção: controles compactos de conteúdo interno podem usar 36 px quando a densidade for importante.

## 5. Hierarquia de botões

### Primário

Uso:
- Iniciar;
- Parar quando é a ação principal do estado atual;
- confirmação positiva de dialog;
- Baixar e atualizar.

Regra: no máximo uma ação primária por grupo lógico.

### Secundário

Uso:
- Reiniciar;
- Depurar;
- Build;
- Sync;
- Salvar quando não é a ação dominante do fluxo.

### Ícone

Uso:
- abrir navegador;
- abrir terminal;
- selecionar pasta/arquivo;
- tema;
- configurações;
- menu de mais ações.

Todo botão apenas com ícone precisa:
- tooltip;
- nome acessível;
- foco de teclado;
- estado disabled perceptível.

### Fantasma

Uso:
- ações de baixa ênfase;
- controles de sidebar;
- ações auxiliares que não devem competir com o fluxo principal.

### Perigo

Uso:
- excluir perfil;
- apagar dados;
- ação destrutiva irreversível.

Não usar vermelho apenas para “Parar”; parar um processo é uma ação normal do produto, não destrutiva de dados.

## 6. Barra de ações do perfil

Ordem visual oficial:

1. **ação primária de ciclo de vida** — Iniciar ou Parar;
2. Reiniciar / Depurar quando aplicáveis;
3. separador e abrir navegador;
4. abrir terminal quando aplicável;
5. menu `…` com **Build completo** e **Sincronizar agora**, além das ações anteriores.

Ao iniciar projetos Tomcat recém-criados, a preparação Maven e o Sync podem trabalhar automaticamente. Nunca remover suas ações de recuperação da CLI ou do menu. A interface exibe o estado de preparação durante o processo. Configurações secundárias do Tomcat (JMX/AJP, Maven individual, VM args, healthcheck e publicação direta) ficam em seções expandíveis, preservando os valores ao salvar.

`Salvar` deve ficar próximo da edição/configuração e só ganhar ênfase quando houver alteração pendente.

A barra não deve crescer indefinidamente. Ações raras devem migrar para `…`.

## 7. Estados de execução

Os estados oficiais de UX serão:

| Estado | Cor | Significado |
|---|---|---|
| Parado | Off | nenhum processo do perfil ativo |
| Iniciando | Warn | processo iniciado, ainda não pronto |
| No ar | Ok | processo gerenciado e pronto |
| Depurando | Dbg | processo em modo debug |
| Build | Warn | build em andamento |
| Reiniciando | Warn | transição controlada |
| Executando externamente | Dbg/neutral | serviço detectado fora do controle do Pitstop |
| Porta ocupada | Warn | porta em uso, identidade do processo não confirmada |
| Erro | Err | falha real de operação |

Importante: **Porta ocupada não é No ar**. A UI deve evitar verde quando apenas a ocupação da porta foi detectada.

O mesmo vocabulário deve ser usado em:
- sidebar;
- pill do cabeçalho;
- tray;
- notificações;
- web;
- CLI quando possível.

## 8. Campos

Nos campos de diretório, desktop e web devem oferecer seleção guiada por um botão de pasta; o campo continua editável para quem conhece o caminho. A interface web lista somente diretórios pela API local protegida, sem ler conteúdo de arquivos. Arquivos WAR/ZIP/JAR são campos de arquivo e não devem receber o seletor de diretórios.

Estrutura padrão:

```text
Rótulo
[ valor / placeholder                         ação ]
Texto auxiliar opcional
Mensagem de validação, quando necessária
```

Regras:
- labels sempre visíveis quando o significado não for trivial;
- placeholder é exemplo, nunca substituto do label;
- paths, portas, URLs, comandos e argumentos usam mono;
- ajuda técnica fica abaixo do campo, não dentro do placeholder;
- erro deve dizer como corrigir;
- botões de escolher pasta/arquivo devem ser focáveis;
- desktop deve associar semanticamente label e controle;
- web usa `<label>` real.

### Campos avançados

Itens como JMX, AJP, VM args, DocBase, origem de conf, readiness avançado e opções raras devem ser agrupados em **Avançado** quando isso reduzir ruído sem esconder uma configuração necessária.

## 9. Tabs

Visual:
- altura 48 px;
- texto Mut quando inativa;
- texto Fg + borda Acc quando ativa;
- sem fundo de card adicional.

Comportamento:
- desktop: foco visível e navegação previsível;
- web: padrão ARIA completo com ←/→, Home/End e roving tabindex;
- mudar de aba não deve perder alterações do formulário.

## 10. Cards e métricas

Cards de resumo devem responder rapidamente:
- qual porta?
- qual debug?
- qual último build?
- qual alvo/URL?

Padrão:
- fundo Surf;
- borda Line;
- radius 12;
- label 12 px Mut;
- valor 18 px mono;
- evitar cards vazios quando não agregam informação.


## 11. Log

O log é uma superfície técnica e deve priorizar leitura.

Padrão:
- fundo próprio `LogBg`;
- JetBrains Mono;
- 12–13 px;
- line-height confortável;
- cores reservadas a tipo/gravidade;
- busca sempre disponível;
- seguir automático como toggle;
- limpar como ação secundária.

### Empty state

Quando não houver linhas:

**Nenhum log ainda**
Os logs deste perfil aparecerão aqui depois que ele for iniciado.

Se o perfil estiver parado, pode complementar com:

**Clique em Iniciar para executar o perfil.**

Evitar área grande completamente vazia sem explicação.

## 12. Sidebar

Manter:
- largura desktop de 280 px;
- marca no topo;
- lista de perfis;
- status resumido;
- rodapé com estado da tela web, ajustes e tema.

Regras:
- perfil selecionado = fundo Soft, nunca apenas diferença de texto;
- estado deve usar ponto + texto, não depender só da cor;
- nomes longos usam ellipsis + tooltip;
- botão `+` mantém criação de perfil;
- agrupamentos futuros só devem ser introduzidos se a quantidade de perfis justificar.

## 13. Dialogs, assistente e confirmações

### Dialog padrão

- largura aproximada: 440–508 px;
- padding: 24 px;
- radius: 14 px;
- título 18 px;
- texto de apoio 13–14 px;
- ações alinhadas à direita;
- Cancelar antes da ação confirmatória;
- foco inicial previsível;
- Escape fecha quando seguro.

### Assistente

Manter estrutura de passos, mas cada página deve:
- apresentar uma decisão por vez;
- evitar parágrafos longos;
- explicar ferramentas opcionais sem sugerir que todas são obrigatórias;
- mostrar claramente o que será alterado.

## 14. Menus e ações contextuais

Menu `…`:
- ações normais primeiro;
- separador antes de ações destrutivas;
- excluir por último e em Err;
- atalhos podem aparecer no lado direito quando existirem.

Web:
- se usar `role="menu"`, implementar comportamento de teclado completo;
- caso contrário, usar semântica de disclosure/lista de botões.

## 15. Textos oficiais

### Termos mantidos

- Build
- Sync
- Debug / Depurar conforme contexto
- JMX
- AJP
- PATH
- Tomcat
- Maven
- npm

Termos técnicos conhecidos do público não precisam ser artificialmente traduzidos.

### Ajustes de microcopy planejados

| Atual / técnico | Direção oficial |
|---|---|
| fora do Pitstop | Executando externamente |
| porta em uso (...) | A porta X está em uso por outro processo ou perfil. |
| Pronto (log) | Pronto quando o log corresponder |
| PATH extra | PATH adicional |
| wars exploded | WARs explodidos / aplicações explodidas |
| perfil sem URL (...) | Este perfil não tem uma URL configurada. |

Tooltips devem explicar ações:
- **Build** — Compilar e atualizar os artefatos.
- **Sync** — Sincronizar arquivos alterados com o Tomcat.
- **Abrir terminal** — Abrir um terminal na pasta deste perfil.

## 16. Acessibilidade

### Desktop

Obrigatório na revisão:
- todos os botões interativos alcançáveis por Tab;
- remover `Focusable=false` de ações importantes;
- botões apenas com ícone com `AutomationProperties.Name`;
- associar labels e inputs com `AutomationProperties.LabeledBy` ou nome equivalente;
- foco visível em todos os controles;
- ordem de Tab correspondente à ordem visual;
- Enter/Space acionam botões;
- Escape fecha dialogs/flyouts seguros;
- não depender apenas de cor para estados.

### Web

Obrigatório:
- tabs com navegação por setas + Home/End;
- menu button com Enter/Space/Escape/setas quando mantiver ARIA menu;
- dialogs devolvem o foco ao gatilho;
- mensagens importantes anunciáveis;
- foco nunca fica preso em elemento oculto;
- `:focus-visible` preservado.

## 17. Responsividade e escala

### Desktop nativo

Validar:
- 980 × 620;
- 1320 × 860;
- maximizado;
- escala Windows 100%, 125%, 150% e 200%;
- texto longo;
- tema claro/escuro;
- alto contraste quando possível.

Ações de cabeçalho devem quebrar/redistribuir sem sobrepor o título.

### Web

Breakpoint atual: 960 px.

Comportamento mantido:
- sidebar vira bloco superior;
- perfis passam a lista horizontal;
- grids de 5 colunas caem para 2;
- grids de 2 colunas caem para 1.

Na revisão devem ser testados também aproximadamente:
- 1440 px;
- 1024 px;
- 768 px;
- 390–430 px.

## 18. Inventário de telas

### Desktop

1. janela principal / nenhum perfil;
2. perfil Tomcat — Log;
3. perfil Tomcat — Artefatos;
4. perfil Tomcat — Servidor;
5. perfil WAR;
6. perfil Aplicação Java;
7. perfil Pacote Java;
8. perfil npm;
9. perfil Comando;
10. criação de perfil;
11. renomear;
12. exclusão;
13. Ajustes;
14. Atualizações;
15. Assistente inicial;
16. menus da bandeja;
17. notificações/toasts;
18. erros/validações;
19. Setup Windows.

### Web

1. nenhum perfil;
2. lista/sidebar;
3. cabeçalho e ações;
4. Log;
5. Artefatos;
6. Servidor;
7. formulários por tipo;
8. novo perfil;
9. menu de ações;
10. Ajustes;
11. dialogs;
12. toast/mensagens;
13. layout responsivo.

## 19. Paridade entre Desktop, Web e CLI

Uma diferença de capacidade deve ser deliberada.

| Recurso | Desktop | Web | CLI |
|---|---:|---:|---:|
| Iniciar/parar | sim | sim | sim |
| Reiniciar | sim | sim | sim |
| Build | por tipo | por tipo | por tipo |
| Debug | por tipo | por tipo | por tipo |
| Sync | Tomcat | Tomcat | Tomcat |
| Logs | sim | sim | sim |
| Editar perfil | sim | sim | não |
| Abrir terminal | sim | não | não |
| Abrir navegador | sim | sim | não |
| Atualizar Pitstop | sim | informar uso do app | não |
| Autostart do app | sim | informar uso do app | não |

A implementação futura deve centralizar no Core as capabilities e defaults para reduzir drift.


## 20. Divergências encontradas no baseline

Estas diferenças são conhecidas antes da implementação da revisão:

1. Desktop usa botão padrão de 40 px; web usa 44 px.
2. Desktop usa input de 42 px; web usa 44 px.
3. Alguns botões compactos desktop usam 34 px enquanto web usa 36 px.
4. Botões de browse adicionados aos campos nativos podem receber `Focusable = false`.
5. Labels da UI nativa são visualmente associados aos inputs, mas em geral não possuem associação de automação explícita.
6. Web declara semântica ARIA de tabs sem implementar toda a navegação por setas/Home/End.
7. Web usa `role="menu"` em menus click-first sem todo o comportamento esperado de menu por teclado.
8. O estado “porta em uso” pode ser visualmente confundido com “No ar”.
9. O log vazio não comunica por que a área está vazia.
10. A barra de ações pode ficar densa em perfis com muitas capabilities.
11. Alguns textos ainda usam linguagem de implementação, por exemplo “fora do Pitstop” e “Pronto (log)”.
12. Desktop e web têm defaults/capabilities parcialmente duplicados.
13. O Setup Windows ainda segue uma linguagem visual própria e mais antiga que a do aplicativo principal.
14. README ainda possui uma referência residual a portátil Windows `.tar.xz`, embora o formato atual seja `.zip`.

## 21. Critérios de aceite da revisão visual

Uma tela só é considerada revisada quando:

- segue os tokens documentados;
- nenhuma ação principal muda de lugar sem justificativa;
- todos os estados possuem texto, não apenas cor;
- controles interativos possuem hover, pressed, focus e disabled coerentes;
- foco de teclado é visível;
- rótulos e mensagens são claros;
- tema claro e escuro funcionam;
- não há corte no tamanho mínimo suportado;
- desktop e web usam o mesmo termo para a mesma ação;
- nenhuma funcionalidade existente foi removida;
- build e smoke continuam verdes.

## 22. Ordem de implementação visual

1. normalizar dimensões/tokens;
2. estados e badges;
3. barra de ações;
4. empty states;
5. labels, campos e textos;
6. seções avançadas;
7. dialogs e menus;
8. desktop accessibility;
9. web accessibility;
10. responsividade;
11. Setup Windows;
12. QA comparativo final.

Mudanças arquiteturais profundas não devem ser misturadas com ajustes puramente visuais quando isso dificultar validar regressões.

## 23. Regra para novas telas

Antes de criar um novo componente ou estilo, verificar se já existe:
- `Button.b`, `pri`, `fantasma`, `perigo`, `ico`;
- `TextBox.inp`;
- card/bloco/stat;
- tab;
- pill;
- menu/flyout;
- dialog padrão.

Se um novo padrão for realmente necessário, documentá-lo aqui e implementá-lo tanto quanto possível nas duas UIs.
