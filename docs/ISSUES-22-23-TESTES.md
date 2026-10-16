# Issues #22 e #23 — plano de validação

Esta melhoria reúne a seleção de projetos Maven de diretórios independentes no mesmo Tomcat e os argumentos JVM persistentes em cada perfil. O formato existente de config/perfis.json é preservado.

## Testes automatizados

No repositório, executar:

```powershell
dotnet build .\src\Pitstop.slnx -c Release
dotnet run --project .\tests\Pitstop.Integration\Pitstop.Integration.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\runner-web-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\ui-smoke.ps1
```

A integração cobre descoberta de dois repositórios externos, nomes iguais em caminhos diferentes, entradas duplicadas, repositório offline, dois contextos Tomcat, invalidação de cache e propagação de CATALINA_OPTS nos modos normal e JPDA. Os smokes verificam API, CLI, preparo, Sync e presença dos controles na web.

## Cenário isolado de interface

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\abrir-cenario-22-23.ps1
```

Abrir http://127.0.0.1:18888/ com o script em execução. O cenário cria, em %TEMP%, os repositórios fictícios workspace-a/sistema e workspace-b/api, com contexto /sistema e /api, no perfil Exemplo-22-23. Na aba **Artefatos**, verificar ambos, adicionar outra pasta Maven pelo botão **Adicionar projeto**, salvar, recarregar, desmarcar um projeto e conferir que o outro permanece. Na aba **Servidor**, confirmar que a propriedade -Dvalidar-xxxxx=true persiste após salvar, atualizar e reabrir a interface.

O script não toca em configs, bases, logs ou processos de uma instalação existente. Interromper o terminal com Ctrl+C para encerrar a instância isolada. A pasta temporária permanece para inspeção, podendo ser removida manualmente depois.

## Teste final com Tomcat real (separado)

1. Usar um novo perfil de teste com um JDK e um Tomcat reais, em porta livre; não testar em perfis de trabalho.
2. Incluir dois projetos Maven WAR válidos, de pastas distintas, e definir contextos HTTP diferentes (/sistema e /api).
3. Ativar preparação automática; iniciar e verificar HTTP 2xx/3xx nos dois endereços. Depois editar um arquivo de um dos projetos e conferir o Sync.
4. Informar -Dvalidar-xxxxx=true em Servidor, salvar e reiniciar. Verificar com um endpoint de teste ou jcmd que System.getProperty("validar-xxxxx") está presente.
5. Testar Debug, desligamento, projeto offline e conflito de contextos. Confirmar que nenhum projeto desaparece da configuração ao ficar indisponível.
6. Repetir com interface desktop e web. Fazer backup de perfis antes de validar em repositórios próprios.

**Importante:** o cenário de interface não fornece Tomcat nem JDK. Não pressionar Iniciar esperando um Tomcat real sem configurar essas ferramentas. A versão existente do Pitstop não é instalada ou substituída por esse script.

## Arquivos principais

- src/Pitstop.Core/Projetos.cs: agrega repositórios selecionados aos encontrados na pasta principal e informa os indisponíveis
- src/Pitstop.Core/Servidor.cs: consulta GET /api/projetos aceita repos repetidos na query
- src/Pitstop.App/Janela.Paineis.cs: cadastro desktop, persistência e UI JVM
- web/ui.html: cadastro web, preservação de projetos ausentes e UI JVM
- tests/Pitstop.Integration/Program.cs e tests/smoke.ps1: cenários de regressão

Os argumentos JVM continuam gravados como vmArgs e enviados via CATALINA_OPTS, sem introduzir outra fonte de configuração. Os projetos continuam salvos no array projetos e os artefatos no array artefatos.
