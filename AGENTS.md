# Guia para agentes de IA — Pitstop

O Pitstop é um gerenciador local de ambientes de desenvolvimento para Windows/Linux.
Antes de alterar ou iniciar qualquer perfil, descubra o estado atual e as ações disponíveis.

## Descoberta

- Execute "pit capabilities" para receber o catálogo de ferramentas MCP em JSON.
- Execute "pit --help" para comandos de terminal e "pit status" para estado dos perfis.
- Caso o cliente suporte MCP, configure "pit mcp" como servidor **stdio**.
- O MCP acessa a instância local do Pitstop aberta na janela desktop ou via "pit ui".
- A documentação detalhada e os exemplos estão em docs/IA-MCP.md.

## Regras de operação

- Consultas de status, perfis, projetos, descoberta e logs podem ser feitas sem alterações.
- Antes de iniciar, parar, reiniciar, compilar, sincronizar, alterar/excluir perfis,
  mudar ajustes ou encerrar o Pitstop, verifique a intenção do usuário.
- Não divulgue dados privados dos perfis, caminhos sensíveis ou conteúdo de .env.
- Jamais remova config/, .env, cache/, logs/ ou bases/ como parte de limpeza do Git.
- Não suponha que "pit up <perfil>" seja não bloqueante: ele mantém o processo no terminal.
  Para ações não bloqueantes com uma instância já aberta, use "pit mcp" e "pitstop_action".
- Não habilite portas externas para acessar a API: a interface é local (127.0.0.1).
- Se o serviço não estiver aberto, explique a necessidade de abrir Pitstop ou "pit ui".
- Perfil "comando" pode executar programas arbitrários previamente configurados: exija cuidado.

## Modos de uso

- CLI: pit status, pit projetos, pit ajustes, pit up, pit down, pit restart,
  pit build, pit sync, pit logs, pit ui, pit capabilities, pit mcp.
- MCP: descobrir ferramentas via tools/list, executar via tools/call.
- Desktop/web: ferramentas adicionais de criação, edição e descoberta são expostas
  pela API local e encapsuladas no MCP. Perfis Comando podem executar scripts locais.

## Desenvolvimento

- Fontes: src/Pitstop.Core, src/Pitstop.Cli, src/Pitstop.App.
- Não edite nem teste processos/instalações reais quando um ambiente isolado é possível.
- Build CLI: dotnet build src/Pitstop.Cli/Pitstop.Cli.csproj -c Release.
- Teste da integração IA: powershell -File tests/mcp-smoke.ps1.
- Testes gerais: dotnet run --project tests/Pitstop.Integration -c Release
  e powershell -File tests/smoke.ps1.

---

## MCP Compatibility

Preserve stdout exclusivamente para tráfego de protocolo e coloque diagnósticos no stderr.
Mantenha schemas e anotações read-only/destrutivas precisas.
Execute `tests/mcp-smoke.ps1` em sua raiz isolada.
Teste comportamento de UI/API indisponível e nunca use perfis reais para testes de smoke.

## Safe Operation

Execute `pit capabilities` e inspecione status antes de ação.
Chamadas read-only de status/descoberta/logs são seguras.
Pergunte antes de start, stop, restart, build, sync, save/delete/rename profile, settings ou shutdown.
Nunca exponha `.env`, segredos de perfis ou porta 9999 além de loopback.

## Release

Leia `docs/BUILD-RELEASE.md`.
Garanta que versão e tag futura correspondam.
Execute testes de integração/smoke/package, verificações de privacidade, notices e geração SHA-256.
Preserve diretórios de dados do usuário.
Nunca crie/push tag ou GitHub Release sem aprovação explícita.

Consulte tambem .agents/README.md deste projeto.
