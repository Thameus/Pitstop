# Pitstop para agentes de IA (CLI e MCP)

## Objetivo e limitações

O Pitstop permite que assistentes de programação usem as mesmas funções do aplicativo
sem clicar na interface. Uma IA precisa ter **acesso autorizado ao computador**
e conhecer a instalação do Pitstop. Nenhuma instalação é detectada magicamente
em outro computador; cada cliente MCP precisa de uma configuração inicial.

O executável "pit" oferece dois pontos de entrada:

- "pit capabilities": catálogo JSON de todas as ferramentas, nomes e esquemas.
- "pit mcp": servidor MCP stdio (JSON-RPC); a IA chama ferramentas por nome.

"pit mcp" não abre portas; ele acessa apenas a API HTTP do Pitstop em 127.0.0.1,
usando a porta RUNNER_PORTA da mesma instalação (padrão 9999).

## Pré-requisitos

1. Instalar Pitstop no computador e disponibilizar pit ou pit.cmd no PATH,
   ou informar seu caminho absoluto no cliente.
2. Deixar o Pitstop aberto no desktop, ou iniciar "pit ui" em um terminal.
3. Registrar um servidor MCP de transporte **stdio** no aplicativo de IA usado.
4. Autorizar explicitamente operações que modificam ambientes e configurações.

O assistente pode chamar pitstop_status para verificar a comunicação.

## Configuração genérica do MCP

Exemplo de configuração para clientes que usam mcpServers:

Windows:

~~~json
{
  "mcpServers": {
    "pitstop": {
      "command": "C:\\CAMINHO\\DO\\PITSTOP\\app\\pit.exe",
      "args": ["mcp"]
    }
  }
}
~~~

Linux:

~~~json
{
  "mcpServers": {
    "pitstop": {
      "command": "/caminho/do/pitstop/app/pit",
      "args": ["mcp"]
    }
  }
}
~~~

A localização e a sintaxe do arquivo de configuração dependem do cliente de IA.
Alguns aplicativos usam uma propriedade servers ou type: stdio em vez de
mcpServers. Configure o equivalente indicado pela documentação de cada cliente.

Não configure uma URL pública nem exponha a porta 9999 na internet.

## Catálogo MCP

| Ferramenta | Ação |
|---|---|
| pitstop_status | Estado de todos os perfis |
| pitstop_profiles | Lista nomes/tipos |
| pitstop_profile | Detalhes de um perfil |
| pitstop_save_profile | Cria ou atualiza perfil |
| pitstop_delete_profile | Exclui perfil |
| pitstop_rename_profile | Renomeia perfil parado |
| pitstop_action | start, debug, stop, reiniciar, build, sync, preparar, abrir, terminal, limpar |
| pitstop_logs | Últimas linhas do buffer da instância ativa |
| pitstop_projects | Detecta repositórios Maven |
| pitstop_inspect | Analisa projeto Maven/npm em uma pasta |
| pitstop_folders | Lista pastas para navegação |
| pitstop_tomcats | Instalações Tomcat encontradas |
| pitstop_java_mains | Classes main de módulo Java |
| pitstop_npm_scripts | Scripts npm por perfil |
| pitstop_package_versions | Pacotes e versões |
| pitstop_settings | Consulta ajustes globais |
| pitstop_update_settings | Altera ajustes conhecidos |
| pitstop_show_window | Mostra janela desktop se disponível |
| pitstop_shutdown | Encerra Pitstop e seus serviços |

As operações de escrita usam a mesma API e as mesmas validações da interface.
O fluxo de save/delete mantém a checagem de versão do perfis.json, evitando
sobrescrever alterações simultâneas. Salvar um perfil preserva os campos não
informados, e excluir um perfil em execução é recusado.

## Exemplos em linguagem natural

- "Pitstop, quais servidores estão no ar?" → pitstop_status.
- "Mostre o log do perfil SGF" → pitstop_logs com perfil: SGF.
- "Faça o build de meu-app" → pitstop_action com acao: build.
- "Reinicie meu-tomcat" → pitstop_action com acao: reiniciar.
- "Detecte o projeto na pasta informada" → pitstop_inspect.
- "Crie um perfil npm nesta pasta" → pitstop_save_profile, após inspeção.

## Segurança e limites operacionais

O processo "pit mcp" deve ser iniciado somente por um cliente de IA **confiável**,
sob a conta local do usuário. Como pode alterar perfis de comando e iniciar processos,
não disponibilize esse MCP a terceiros ou agentes sem confirmação de ações sensíveis.

- As ações start/debug/build podem continuar em segundo plano no runner;
  consulte pitstop_status e pitstop_logs depois de iniciá-las.
- pitstop_logs lê o replay disponível na memória da instância ativa,
  limitado a 200 linhas por consulta; não dá streaming contínuo.
- O comando "pit up" continua intencionalmente ligado ao terminal atual.
- As versões da CLI/MCP e da instância desktop devem ser compatíveis,
  preferencialmente da mesma instalação.
- Se o Pitstop não estiver aberto, tools/list ainda funciona, mas chamadas
  que precisam da API retornam erro explicativo.
- A API HTTP local continua isolada em loopback; o MCP não é acesso remoto.
- O atualizador de versões e o controle de inicialização automática do Pitstop
  permanecem funcionalidades da interface desktop, não ferramentas MCP.
- Não use perfis de comando como substituto de controle de acesso do sistema.
- Nunca copie .env ou config/perfis.json para prompts, logs ou repositórios
  sem revisar a presença de senhas, tokens e caminhos privados.

## Testar

No repositório:

~~~powershell
dotnet build .\src\Pitstop.Cli\Pitstop.Cli.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\mcp-smoke.ps1
~~~

O teste cria uma raiz temporária e inicia um servidor HTTP isolado;
não altera os perfis ou serviços de uma instalação em uso.
