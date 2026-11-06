# Issue #41 — medição de memória

O status agora informa `memoriaBytes` e `estadoMemoria` (`disponivel`, `indisponivel` ou
`semPid`) somente para uma execução cujo PID esteja validado. No Windows a soma usa o
working set da raiz e descendentes; no Linux usa RSS de `/proc`. Falha em validar a
raiz ou em ler a árvore inteira retorna nulo, nunca zero ou soma parcial.

Perfis identificados apenas por porta ocupada não recebem memória. A medição é feita
uma vez por perfil em cada consulta de status, sem WMI nem sondas adicionais; a
consulta já ocorre em paralelo e em baixa frequência nas interfaces.

CLI, UI web e UI desktop apresentam `RAM: <valor>` quando disponível e `RAM: indisponível`
nos demais estados, sem alterar o contrato nullable do JSON.

Pendente para uma alteração posterior: aviso automático de muitos perfis e limites de
memória. Isso exige uma arquitetura de notificações/limites e continua fora desta issue.
