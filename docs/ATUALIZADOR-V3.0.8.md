# Atualização 3.0.8 — segurança e testes

O Pitstop usa um helper externo (`app/updater/Pitstop.Updater.exe`) que é copiado para o diretório de download antes do fechamento do aplicativo. A interface inicia esse helper, encerra o Pitstop e o helper espera o PID original finalizar. A versão 3.0.8 aprimora esse fluxo sem alterar o formato de instalação ou exigir Electron.

## Mudanças

- A extração e a cópia preliminar ocorrem antes da primeira mudança nos diretórios instalados.
- O helper atualiza `app`, `web`, `third-party` e `docs` como uma transação lógica. Se algum passo falhar, restaura os diretórios já trocados na ordem inversa e restaura os arquivos da raiz.
- Diretórios de trabalho e backup usam nomes exclusivos `.update-new-<id>` e `.update-old-<id>`, para não colidir com tentativas anteriores.
- As configurações `.env`, `config/perfis.json`, `cache` e perfis Maven/Tomcat não fazem parte do payload e não são substituídos.
- Falhas de cópia mostram caminho e operação; a detecção de módulos carregados sugere processos e PIDs quando possível. Não encerra aplicativos de terceiros.
- Log persistente em `%LOCALAPPDATA%\Pitstop\updates\updater.log`, fora da pasta temporária que é removida depois da atualização.
- Um teste propositalmente falha após a primeira troca e verifica que app, web e configurações antigas são recuperados.

## Testar

```powershell
dotnet build .\src\Pitstop.slnx -c Release
dotnet run --project .\tests\Pitstop.Integration\Pitstop.Integration.csproj -c Release
powershell -File .\empacotar.ps1
powershell -File .\tests\smoke-package.ps1
```

O smoke do pacote executa `--smoke-blocker`, `--smoke-update` e `--smoke-rollback` com destinos temporários. Não atualiza a instalação real.

## Limites conhecidos e fallback

O Windows pode bloquear uma pasta quando um terminal, antivírus ou serviço externo mantém handles abertos. O novo helper espera, tenta novamente e informa o caminho da falha; ele não encerra à força processos de terceiros. Se não conseguir liberar a instalação, mantém a versão anterior sempre que o rollback tiver sucesso. Pode ser necessário fechar o processo externo e tentar novamente, ou atualizar manualmente usando o instalador oficial.

A primeira atualização de **3.0.7 para 3.0.8** começa no helper da **3.0.7**, que ainda não possui as correções incluídas na 3.0.8. Caso esse primeiro passo falhe, utilizar uma vez o novo instalador 3.0.8; atualizações posteriores usarão o helper melhorado.

Uma estratégia de diretórios versionados e launcher fixo continua candidata para evolução futura, mas não foi introduzida nesta versão para evitar migrar atalhos, PATH, CLI e MCP sem validação. O Linux mantém o atualizador próprio do .run.
