# Pitstop.SixTypes

Suíte executável determinística para os seis tipos de perfil (`tomcat`, `war`,
`java`, `zip`, `npm` e `comando`) e para vários perfis simultâneos.

```powershell
dotnet run --project tests/Pitstop.SixTypes -c Release
```

Todo o material é criado sob uma pasta temporária exclusiva. O teste remove a
pasta e restaura `PIT_RAIZ` em `finally`; não usa `config/`, `bases/`, `cache/`
ou instalações do usuário.

O perfil artificial `foreign` registra no JSON a mesma porta de um listener
externo, mas não possui `pit.pid` nem execução no Runner. Ele é incluído no
`PararTodos`; depois da parada em lote o teste abre uma conexão aceita pelo
listener, comprovando que a porta ocupada não é usada como ownership nem como
protocolo de shutdown.

Limitação deliberada: Java, Node/npm e Tomcat não são iniciados. Java/zip e npm
cobrem normalização, validação, builders, ambiente e extração; a execução de
comando cobre o ciclo real de processos. O fixture cria somente executáveis/
arquivos mínimos fictícios para satisfazer validações, não uma JVM, Node ou
servidor Tomcat funcional.

`status --json` é validado em uma invocação real do CLI (JSON puro, perfis,
`base` e `estadoMemoria`). A linha de falha é verificada no `Runner` que possui
a execução em memória; o CLI lançado como processo separado naturalmente não
compartilha o último resultado em memória.
