#!/bin/sh
# Remove o Pitstop instalado pelo instalar.sh: comandos em ~/.local/bin, atalho do menu, ícone, "iniciar com a
# sessão" e os arquivos do programa. Seus dados (.env, config/, cache/, logs/, bases/) só saem se você confirmar.
# Uso: sh desinstalar.sh [--sim] [--apagar-dados]
set -eu
PASTA=$(cd "$(dirname "$0")" && pwd)
DADOS="${XDG_DATA_HOME:-$HOME/.local/share}"
CONF="${XDG_CONFIG_HOME:-$HOME/.config}"
SIM=0; APAGAR=0
for a in "$@"; do
  case "$a" in --sim|-y) SIM=1 ;; --apagar-dados) APAGAR=1 ;; esac
done

if pgrep -f "$PASTA/app/Pitstop" >/dev/null 2>&1; then
  echo "O Pitstop está aberto. Feche pelo ícone da bandeja ou menu ⋯ da janela (Parar e Sair) e rode de novo."
  exit 1
fi

for l in "$HOME/.local/bin/pitstop" "$HOME/.local/bin/pit"; do
  if [ -L "$l" ] && readlink "$l" | grep -q "^$PASTA/"; then rm -f "$l"; fi
done
rm -f "$DADOS/applications/pitstop.desktop" "$DADOS/icons/hicolor/256x256/apps/pitstop.png"
if [ -f "$CONF/autostart/pitstop.desktop" ] && grep -q "$PASTA/" "$CONF/autostart/pitstop.desktop"; then
  rm -f "$CONF/autostart/pitstop.desktop"
fi
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DADOS/applications" >/dev/null 2>&1 || true

if [ "$APAGAR" = 0 ] && [ "$SIM" = 0 ]; then
  printf 'Apagar também seus perfis e ajustes (.env, config, cache, logs, bases)? [s/N] '
  read -r r || r=""
  case "$r" in s|S|sim|y|Y) APAGAR=1 ;; esac
fi
cd "$PASTA"
for f in * .env.exemplo; do
  case "$f" in
    config|cache|logs|bases) [ "$APAGAR" = 1 ] && rm -rf "$f" ;;
    *) [ -e "$f" ] && rm -rf "$f" ;;
  esac
done
[ "$APAGAR" = 1 ] && rm -f .env
cd / && rmdir "$PASTA" 2>/dev/null || true
if [ "$APAGAR" = 1 ]; then echo "Pitstop removido."; else echo "Pitstop removido. Seus dados ficaram em $PASTA."; fi
