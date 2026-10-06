#!/bin/sh
# Desenvolvimento no Linux: publica o Pitstop em app/ (Pitstop + pit). Precisa do .NET 10 SDK.
# Para gerar os pacotes de distribuição use empacotar.ps1 (Windows) — ele gera o pacote Linux também.
set -eu
cd "$(dirname "$0")"
command -v dotnet >/dev/null || { echo "dotnet não encontrado no PATH (instale o .NET 10 SDK)"; exit 1; }
if pgrep -f "$(pwd)/app/Pitstop" >/dev/null 2>&1; then echo "O Pitstop está aberto: feche (Parar e Sair) e rode de novo."; exit 1; fi
rm -rf app
dotnet publish src/Pitstop.App/Pitstop.App.csproj -c Release -o app --nologo
dotnet publish src/Pitstop.Cli/Pitstop.Cli.csproj -c Release -o app --nologo
chmod +x app/Pitstop app/pit pit
echo "Pronto. Abra com ./app/Pitstop (janela + bandeja) ou use ./pit no terminal."
