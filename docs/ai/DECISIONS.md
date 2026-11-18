# Decisões

Registro de decisões do projeto: data, pergunta, decisão, motivo.

## 2026-11-18 — Pasta padrão de instalação no Linux

- **Pergunta:** a pasta padrão de instalação no Linux deve mudar de `~/.local/share/pitstop` (minúsculo) para `Pitstop`, como no Windows?
- **Decisão:** o padrão continua `~/.local/share/pitstop` (minúsculo). Reinstalar na pasta de uma instalação existente atualiza no lugar, nunca cria uma pasta aninhada dentro dela.
- **Motivo:** o sistema de arquivos do Linux diferencia maiúsculas de minúsculas e já existem instalações com o nome minúsculo; mudar o padrão duplicaria instalações.

## 2026-11-18 — RAM exibida apenas para perfis em execução

- **Pergunta:** quando e como mostrar o uso de RAM de um perfil?
- **Decisão:** a RAM só é mostrada para perfis em execução. Perfil em execução cuja memória não pôde ser medida mostra "RAM: indisponível".
- **Motivo:** perfil parado não tem consumo a mostrar, e "indisponível" evita exibir 0 ou valor enganoso quando a medição falha.

## 2026-11-18 — Rotas GET com caminhos locais exigem X-PIT: 1

- **Pergunta:** como tratar os alertas 4 e 5 de path do CodeQL e proteger as rotas GET que recebem caminhos locais?
- **Decisão:** `/api/pastas`, `/api/inspecionar`, `/api/projetos`, `/api/mains` e `/api/scripts` exigem o cabeçalho `X-PIT: 1`. Os alertas 4 e 5 do CodeQL são falsos positivos: o servidor escuta só em loopback, valida o Host e não habilita CORS.
- **Motivo:** o cabeçalho customizado impede que uma página externa dispare essas leituras de caminhos pelo navegador, e as demais proteções já tornam o fluxo de dados alcançável apenas localmente.
