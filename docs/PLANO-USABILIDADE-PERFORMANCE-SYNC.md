# Pitstop — Plano de usabilidade, desempenho e sincronização automática

Status: planejamento técnico com implementação parcial: a Fase 0 passou por build/smoke e a Fase 1 iniciou com navegador de diretórios na interface web. Sync automático, Maven inteligente e modos sem WAR permanecem planejados.
Data: 2026-10-09. Baseline da revisão: Pitstop 3.0.6 / branch main.
Referências: README.md, DESIGN.md, src/Pitstop.Core, src/Pitstop.App, web/ui.html; SmartTomcat (zengkid/SmartTomcat); Tomcat Resources/Context; Maven WAR Plugin.

## 1. Objetivo e princípios

Permitir que a pessoa escolha um projeto uma única vez e depois use Iniciar/Parar sem executar Build e Sync manualmente a cada sessão. Diminuir o tempo até a aplicação estar funcional, sobretudo nas execuções repetidas, sem sacrificar a correção dos artefatos. Desktop, web, CLI e bandeja compartilham exatamente as mesmas regras.

- Padrão: automatizar tarefas repetitivas, mostrar progresso e oferecer ações avançadas sem poluir a barra.
- Respeitar os perfis existentes e o formato atual do arquivo config/perfis.json.
- Não prometer ganhos sem medição; ter fallback inequívoco para Build completo.
- Alterações em fontes, recursos gerados, dependências e exclusões devem ser refletidas corretamente.
- Nunca iniciar ou atualizar com artefatos incompletos ou parcial/inconsistentemente copiados.
- Sem serviço de monitoramento global permanente: observação só quando o perfil pede.
- Custo financeiro: nenhuma nova dependência obrigatória de serviço pago.

## 2. Baseline confirmado no código (2026-10-09)

- Runner.Iniciar para Tomcat normal publica docBase já existente, sem Maven antes de subir; para aplicação Java o fluxo já prepara Maven.
- Runner.BuildArtefatos executa comando de build de cada artefato; padrão de descoberta usa Maven package.
- Runner.Sync chama Tomcat.CopiarNovos, por mtime e sem deletar destino que sumiu da origem.
- Tomcat.ResolverDocBase escolhe a pasta WEB-INF em target pelo timestamp; saída velha pode ser selecionada.
- Tomcat.PrepararBase escreve contextos em base/conf/Catalina/localhost e recria arquivos XML dos contextos.
- JavaApp.Desatualizados compara maior timestamp de fontes com classes e não detecta toda remoção de arquivo.
- JavaApp.ResolverClasspath mantém cache por alteração de POM; não considera todos os fatores externos.
- Ui.Procurar no desktop já abre seletor de pastas/arquivos; a interface web usa entradas de texto.
- A UI possui barra com Iniciar, Depurar, Parar, Reiniciar, Build e Sync; o DESIGN.md limita o peso visual das ações.
- Tela e CLI já possuem controle de reserva, cancelamento de build, logs e status, passíveis de reutilização.
- Teste local do SGF não foi feito: repositório/POMs não identificados entre os projetos da pasta padrão disponível.

## 3. Experiência final

### Criação de perfil

1. Adicionar projeto -> selecionar pasta no explorador de arquivos ou navegador de diretórios interno.
2. Detectar tipo (Maven com módulos web, Java main, npm, pacote, comando), JDK, Maven, Tomcat e versões.
3. Oferecer um resumo editável: projeto, módulo, contexto, porta, ferramenta e comando de início.
4. Apresentar apenas campos essenciais; ajustes avançados disponíveis em painel expansível.
5. Verificar requisitos antes de gravar sem apagar as opções existentes.

### Operação diária

Barra principal: Iniciar (ou Parar), Depurar quando suportado, Reiniciar, Abrir aplicação e Mais ações. Deslocar Build completo, Sincronizar agora, Revalidar dependências, Terminal quando secundário e informações técnicas para Mais ações/painel de diagnóstico. Manter comandos CLI correspondentes.

Estados: Parado -> Verificando -> Preparando -> Publicando -> Iniciando servidor -> Validando aplicação -> Pronto; também Atualizando, Reiniciando aplicação, Cancelando e Erro. O log detalhado continua acessível; o cabeçalho mostra a etapa e a duração.

### Sincronização inteligente

- Recursos web leves (JSP, HTML, CSS, JS, imagens): sincronização automática e imediata quando houver cópia necessária; no modo direto, servidos da fonte.
- Java (.java, resources compilados): enfileirar compilação incremental após estabilizar edições; nunca iniciar Maven em paralelo para um mesmo perfil.
- Dependências (pom.xml, jars, plugins, perfis): suspender sync leve, revalidar classpath e preparar uma atualização coerente; a aplicação poderá exigir recarga.
- Arquivos removidos: retirar somente arquivos que o próprio Pitstop gerenciou, e só após confirmar estado de origem.
- Classes/JARs em uso: não sobrescrever às cegas. Preparar saídas isoladas e recarregar contexto de forma coordenada; se indisponível, adiar aplicação ou solicitar reinício.

Opção de usuário em nível de perfil: Atualização automática = Inteligente (padrão para Tomcat de desenvolvimento), Ao iniciar, Desativada. Evitar outras opções visíveis na primeira tela. Perfis de pacote pronto não monitoram código-fonte.

## 4. Arquitetura

### 4.1 Núcleo compartilhado

Criar serviços testáveis em Pitstop.Core:
- ProjectInspector: descobre estrutura, módulos, tipo e ferramentas; devolve diagnósticos e sugestões.
- PreparationPlanner: monta etapas do projeto, incluindo módulos e goals especiais.
- PreparationExecutor: orquestra processos Maven, log, progresso, cancelamento, timeout e reserva.
- BuildState: manifesto de entradas/saídas e dependências; escrita atômica em cache/<perfil>, sem dados sensíveis.
- ProjectChangeWatcher: FileSystemWatcher + debounce + varredura de reconciliação (overflow/renomeações).
- SyncEngine: manifesto dos arquivos gerenciados, cópia/exclusão segura e transações quando possível.
- TomcatDeployment: modos Convencional e Direto, mapeamento Resources e compatibilidade por versão.
- ReadinessProbe: processo vivo, sinal Tomcat e endpoint opcional de prontidão configurável.

Os serviços são chamados por Runner e expostos de forma equivalente para app Avalonia, API HTTP, CLI e bandeja. Não criar regras de build diferentes no HTML e na janela nativa.

### 4.2 Estado de build

A chave do cache deve considerar: arquivos relevantes com caminho/tamanho/data e hash quando necessário; presença e exclusão; módulos e ordem; POMs parent/profiles, .mvn, settings efetivo; JDK/Maven e parâmetros; dependências locais, versões e paths; goals/plugins; geração Jasper e replicador; saídas esperadas e integridade. Fast-path por metadados, hash incremental quando houver dúvida. Cache só vale após sucesso e validação.

Garantir invalidação para mudança de branch Git, limpar target, alterar JDK, retirar class, retirar JAR e trocar configuração. Nenhum arquivo de cache pode ser confundido com artefato válido.

### 4.3 Tomcat e Maven

Modo convencional: preservar command build do artefato e docBase explodido; Iniciar executa preparação necessária e só então sobe.

Modo sem WAR: gerar Context/Resources com pasta web e entradas /WEB-INF/classes e /WEB-INF/lib de runtime. Verificar ordem de classload, sobreposições, recursos filtrados, scan de anotação, Servlet/Tomcat e frameworks. Dar fallback automático/explicado quando não suportado. Não ativar allowLinking em Windows.

Alternativa intermediária: Maven war:exploded sem WAR compactado, mas ainda faz trabalho de montagem/cópia. Medir e comparar com ResourceSets.

Regra SGF: não trocar package por compile indiscriminadamente. Mapear goals ligados a fases e plugins Jasper/replicador, inclusões/overlays/transformações e saídas de cada módulo. Preferir recipe de preparação versionada; profile Maven de desenvolvimento é opcional e deve reproduzir artefatos necessários.

### 4.4 Alterações com servidor no ar

Desativar reloadable genérico como padrão até medir custo; usar estratégia por evento:
- arquivo estático -> refletir sem recarga desnecessária;
- arquivo JSP -> seguir ciclo do Jasper/Tomcat e validar atualização;
- classe/resources -> compilar, garantir saída coerente, recarregar contexto quando suportado;
- lib ou contexto -> reinício de aplicação/contexto ou do processo, com aviso;
- HotSwap via JDWP -> investigação separada, sem prometer classes estruturais.

Não reexecutar sincronização em resposta às saídas criadas pelo próprio watcher. Nunca executar duas atualizações simultâneas no mesmo perfil.

## 5. Simplificação por tipo de perfil

| Tipo | Sempre visível | Avançado/automático |
|---|---|---|
| Tomcat de projeto | projeto, módulo/contexto, porta e Iniciar | mavenHome, javaHome, confOrigem, docBase, build recipe, regras Sync, JMX/AJP, VM args |
| WAR pronto | arquivo WAR, contexto e Iniciar | extração, outras versões, portas avançadas, VM args |
| Java main | módulo, classe main e Iniciar | Maven/JDK individual, working dir, args, classpath, debug e flags |
| ZIP/JAR | pacote, versão e Iniciar | jar interno, confDestino, variáveis Java, extra PATH |
| npm | pasta, script e Iniciar | Node Home, build, env, argumentos, regex de prontidão |
| Comando | comando, pasta e Iniciar | shell personalizado, envFile, timeout, prontaLog, auto-start |

Campos vazios devem herdar defaults globais; mostrar de onde veio o valor e um botão de sobrescrita. Não eliminar capacidade nem perder valores ocultos ao salvar. O painel avançado deve ser agrupado por Servidor, Execução, Preparação e Diagnóstico.

## 6. Etapas e critérios de aceite

### Fase 0 — Medição e proteção de baseline
- Capturar tempo por fase, tempo até resposta real, número de arquivos copiados, CPU, RAM e disco.
- Preparar fixtures Tomcat/Maven simples, multi-módulo e semelhante ao SGF.
- Testar funcionalidades atuais de 6 tipos em desktop, web, CLI e tray; registrar comportamento e regressões.
- Aceite: linha de base reproduzível, sem usar dados ou secrets pessoais.

### Fase 1 — UX de cadastro e configuração
- Assistente para selecionar pasta, detectar projeto, módulos e ferramentas, validar pré-requisitos.
- Desktop: reutilizar Ui.Procurar. Web: API read-only de navegação de pastas, limitando raízes expostas e bloqueando traversal/symlinks indevidos; não retornar conteúdo de arquivos.
- Dividir campos em básicos e avançados; defaults globais herdados.
- Aceite: criar projeto Maven/npm com seleção guiada sem colar caminhos; salvar/reabrir sem perda de configuração; teclado e acessibilidade.

### Fase 2 — Iniciar com preparo confiável
- Integrar ao fluxo de Tomcat a execução de comandos de build configurados antes do start; preservar SGF package inicialmente.
- Progresso, logs claros, cancelamento, reservas, tratamento de falha e resumo de tempo.
- Expor modo seguro como padrão compatível; manter build completo manual.
- Aceite: 1 clique prepara e sobe; compilação com erro impede start; cancelar interrompe árvore Maven e não inicia Tomcat.

### Fase 3 — Sync automático seguro
- Watcher somente com perfil relevante ativo, debounce configurado internamente e fila serial.
- Sync por manifesto, inclusões/exclusões, proteção de destino, locks e erros.
- Diferenciar estáticos, Java compilado, libs e recursos gerados.
- Remover botão Sync da barra principal; manter em Mais ações/CLI.
- Aceite: alterar, renomear e remover arquivos atualiza corretamente; sem loops, duplicatas ou copiar parcialmente com arquivo em uso.

### Fase 4 — Cache e preparação incremental
- Persistir e validar fingerprints/manifesto de build, dependências e saídas.
- Pular somente fases comprovadamente válidas; rastrear JARs gerados e recursos Jasper.
- Aceite: zero rebuild sem mudanças quando seguro; qualquer remoção ou mudança relevante invalida a etapa necessária; tempo medido comparado ao baseline.

### Fase 5 — Publicação sem WAR
- Gerar Resources/context XML adequados ao Tomcat/versionamento e à ordem de classe.
- Suportar classes dos módulos e JARs runtime; testar configuração XML, resources, scanning e apps legadas.
- Manter seleção automática/convencional por compatibilidade, sem impor modo direto ao SGF não validado.
- Aceite: aplicação funcionalmente equivalente ao modo convencional; sem dependências faltantes nem classes stale; medir benefício.

### Fase 6 — Atualização em execução
- Atualização estática transparente; atualização Java com reload de contexto coordenado e fallback.
- HotSwap opcional após provas de compatibilidade, sem exigir plugin de IDE.
- Aceite: aplicação permanece operacional ou exibe etapa de recarga identificada; falha conserva última versão íntegra quando possível.

### Fase 7 — Acabamento e release
- Reorganizar navegação, ações e configurações nas duas interfaces; documentação README, DESIGN, LEIAME, exemplos.
- Unit/integration/smoke para 6 perfis, Windows/Linux, CLI/web/desktop/tray; cobertura de cancelamento, concorrência, cache, remoções, caminhos e atualizar.
- QA visual (light/dark, escala, teclado), performance repetida (cold/warm/change), auditoria de segurança e licenças.
- Aceite: build, testes, smoke, checks e release workflow verdes; somente depois commit, push e tag autorizados.

## 7. Matriz de riscos e decisões

| Risco | Mitigação |
|---|---|
| SGF Jasper/replicador não executados ao usar compile | Recipe explícita, comparar saídas e preservar package até comprovar equivalência |
| Código antigo após exclusão | Manifesto + remoção gerenciada / rebuild em inconsistência |
| JAR em uso no Windows | Saída isolada, reload/restart seguro, nunca cópia silenciosamente falhada |
| FileSystemWatcher perde eventos | Reconciliação periódica, debounce e varredura após overflow |
| OneDrive e antivírus provocam alterações/locks | Excluir diretórios gerados, tentativas limitadas, aviso, evitar monitoramento global |
| Contexto XML perde configuração manual | Mesclar e validar contextos gerenciados, não apagar contextos desconhecidos |
| Preview da web expõe diretórios | Restrição de raiz/origem, canonicalização e não retornar conteúdo sensível |
| Cache perde invalidação | Saídas verificadas + hash seletivo + opção Revalidar/Build completo |
| Reload gera overhead/queda | Evitar reloadable genérico por padrão; eventos específicos, medir |
| Perfis antigos perdem opções | Migração backward-compatible e testes de round-trip JSON |
| Uso de computador real causa perturbação | Testes em bases, portas e projetos temporários; SGF somente com autorização e dados protegidos |

## 8. Métricas e validação

Cronometrar no mesmo computador: cold start, warm start sem alteração, modificação de JSP/CSS, classe Java, remoção de classe, mudança de POM/JAR, mudança de branch, fallback para package. Pelo menos 3 rodadas controladas por cenário; registrar mediana, pior caso, tempo Tomcat e tempo de prontidão HTTP, I/O e memória. Ganho de velocidade é meta, não número garantido.

Qualidade obrigatória: sem deploy após erro, sem uso de código stale, sem apagar arquivos não gerenciados, config preservada, cancelamento eficaz, sem processo órfão, sem custo de serviço externo, paridade desktop/web/CLI.

## 9. Pendências externas antes de implementar otimizações SGF

- Localizar repositório SGF, POM pai e módulos web.
- Inspecionar plugins Maven, goals/execuções e perfis ativos; geração Jasper e JARs do replicador.
- Confirmar versão do Tomcat, JDK, Maven e fluxo de desenvolvimento real.
- Registrar tempo atual de package, cópias, startup e app ready.
- Definir a recipe mínima segura; comparar funcionalidade com o pacote completo.

## 10. Estado de implementação

Implementação realizada em 2026-10-09, com evolução incremental e testes repetidos:

| Fase | Estado | Entrega e validação |
|---|---|---|
| 0 — Baseline | Aprovada em Windows, testes automatizados | Build Release de App e CLI, JS, `tests/smoke.ps1`; sem ensaio SGF real |
| 1 — Cadastro guiado | Implementada e parcialmente validada | Escolha de pastas no desktop e na web, inspeção Maven/npm, módulos aninhados; smoke de API e Chrome headless; QA manual pendente |
| 2 — Iniciar inteligente | Implementada; E2E simulado aprovado | Maven configurado antes do Tomcat, cancela/paralisa no erro, preserva `package`, healthcheck HTTP opcional; `tests/runner-web-smoke.ps1` |
| 3 — Sync automático | Implementada; E2E simulado aprovado | FileSystemWatcher com debounce, cópias atômicas e manifesto de exclusões gerenciadas; testada adição/remoção e proteção externa |
| 4 — Cache incremental | Implementada versão conservadora; testes aprovados | Fingerprint de entradas e saídas, detectar adição, exclusão, JAR de target, classes e configuração de ferramenta; não substitui análise completa do Maven reactor/SGF |
| 5 — Sem WAR | Modo experimental opt-in; testes estruturais aprovados | Resources no Tomcat 8+, pastas web e classes diretas, libs da montagem explodida anterior; fallback convencional; equivalência SGF real pendente |
| 6 — Recarga | Parcial/opt-in | Tomcat `reloadable` opcional, desligado por padrão; não implementado HotSwap JDWP ou restart inteligente para todos os frameworks |
| 7 — Acabamento e release | Parcial | Build, teste de integração Core, smoke CLI/API, E2E fake Maven/Tomcat e JS; faltam Linux, aplicação SGF real, QA de interação e release |

**Compatibilidade:** novos perfis Tomcat recebem `prepararAoIniciar: true` e `syncAutomatico: true`; os perfis existentes sem essas chaves permanecem no modo manual até que o usuário habilite as opções. `publicacaoDireta` e `reloadAutomatico` começam desligadas para todos.

**Limites reais:** sem Maven/JDK no PATH do computador e sem o repositório SGF disponível, as integrações de Jasper/replicador, desempenho efetivo, segurança de bibliotecas do SGF e comportamento de reload sob carga não foram verificados. Os testes E2E usam componentes simulados em diretório temporário. Não chamar esta etapa de release validada para SGF.

**Próxima validação de release (bloqueante):** montar SGF e ferramenta reais em ambiente de teste; comparar inicialização convencional e direta, recursos compilados e lib JARs, JSP/Jasper, replicador, classes alteradas e removidas, dependências SNAPSHOT, limites de CPU, arquivos bloqueados no Windows e preservação dos dados de perfil. Validar também Linux desktop/headless.

Arquivos de implementação: `src/Pitstop.Core/{PreparacaoTomcat,SyncTomcat,TomcatDireto,InspecaoProjeto}.cs`, `Runner.cs`, `Tomcat.cs`, `Config.cs`, `Projetos.cs`, `Servidor.cs`, telas desktop/web e testes em `tests/Pitstop.Integration/` e `tests/runner-web-smoke.ps1`.

O ajuste anterior de `web/ui.html` para descobrir projetos apenas sob demanda foi mantido. Não publicar release ou tag antes do teste real e regressão completa.
