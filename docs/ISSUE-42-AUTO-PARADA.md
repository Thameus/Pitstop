# Issue #42 — auto-parada por duração

Perfis podem informar `autoPararAposSeg` como inteiro positivo. O contador começa
somente quando a execução foi marcada como pronta pelo Runner (`prontoLog`,
prontidão HTTP, porta do Comando ou fallback de inicialização). Vazio omite a
função.

O prazo é monitorado por uma única tarefa do Runner e só se aplica a entradas
em `estado[]`, isto é, processos pertencentes à execução atual do Runner. Não
há inferência por porta, log ou readiness, nem parada de processos externos.

Ao encerrar, o resultado recebe `Motivo = "auto-duracao"`; o motivo aparece em
`Ultimo`, no status da API e em `pit status --json`. A opção é independente de
`timeoutExecucaoSeg`, que continua sendo o limite absoluto existente do perfil
Comando.

Não se aplica a processos iniciados por `pit up` ou outra instância, pois eles
não estão acoplados ao Runner atual.
