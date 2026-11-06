# Issue #40 — limite de observabilidade

`falhaLog` é observado pelo pipeline de saída do `Runner` (tela, tray e API local). A primeira linha que contém o texto literal é exposta em `status` como `Execucao.Falha`/`Execucao.LinhaFalha` e, após o processo terminar, em `Ultimo.Falha`/`Ultimo.LinhaFalha`.

Uma execução iniciada diretamente por `pit up` não é acoplada ao `Runner` da tela; ela grava apenas `pit.pid` e não compartilha o estado de linhas com a API. Portanto, esta rodada não promete `falhaLog` para esse caminho direto. Isso evita afirmar uma cobertura que a arquitetura atual não consegue observar com segurança.

A falha não encerra o processo. Um novo início cria uma execução nova, sem a falha anterior.
