#!/bin/sh
# Instalador do Pitstop para Linux (sem root).
# Copia esta pasta para ~/.local/share/pitstop (ou o destino informado), cria os comandos "pitstop" e "pit" em
# ~/.local/bin, o atalho no menu de aplicativos (.desktop) e abre o Pitstop. Reinstalar/atualizar mantém o que é
# seu: .env, config/, cache/, logs/ e bases/.
# Uso: sh instalar.sh [destino] [--sim]   (--sim = não pergunta nada)
set -eu

ORIGEM=${PITSTOP_SOURCE_ROOT:-$(cd "$(dirname "$0")" && pwd)}
DADOS="${XDG_DATA_HOME:-$HOME/.local/share}"
DESTINO="$DADOS/pitstop"
SIM=0
NAO_ABRIR=0
for a in "$@"; do
  case "$a" in
    --sim|-y) SIM=1 ;;
    --nao-abrir) NAO_ABRIR=1 ;;
    *) DESTINO="$a" ;;
  esac
done
BIN="$HOME/.local/bin"

pergunta() { # pergunta "texto" padrao(s|n)
  if [ "$SIM" = 1 ]; then [ "$2" = s ]; return; fi
  if [ "$2" = s ]; then op="[S/n]"; else op="[s/N]"; fi
  printf '%s %s ' "$1" "$op"
  read -r r || r=""
  [ -z "$r" ] && r="$2"
  case "$r" in s|S|sim|y|Y|yes) return 0 ;; *) return 1 ;; esac
}

printf '\n  \033[35mPitstop - instalação\033[0m\n  Runner local de Tomcat, Java, npm e comandos personalizados.\n\n'

[ -f "$ORIGEM/app/Pitstop" ] || { echo "app/Pitstop não encontrado em $ORIGEM (rode de dentro da pasta extraída)"; exit 1; }
case "$(uname -m)" in x86_64|amd64) ;; *) echo "este pacote é para Linux x64 (esta máquina: $(uname -m))"; exit 1 ;; esac

if [ "$SIM" = 0 ]; then
  printf '  Pasta de instalação [%s]: ' "$DESTINO"
  read -r r || r=""
  [ -n "$r" ] && DESTINO="$r"
fi

if pgrep -f "$DESTINO/app/Pitstop" >/dev/null 2>&1; then
  echo "  O Pitstop está aberto. Feche pelo ícone da bandeja ou menu ⋯ da janela (Parar e Sair) e rode de novo."
  exit 1
fi

if [ "$ORIGEM" != "$DESTINO" ]; then
  echo "  Copiando para $DESTINO ..."
  mkdir -p "$DESTINO"
  # app/ e web/ são do programa: troca inteiro (versão nova não deixa arquivo velho para trás)
  rm -rf "$DESTINO/app" "$DESTINO/web"
  cp -R "$ORIGEM/app" "$ORIGEM/web" "$DESTINO/"
  for f in "$ORIGEM"/* "$ORIGEM"/.env.exemplo; do
    [ -f "$f" ] && cp "$f" "$DESTINO/"
  done
  # No repositório os scripts reais ficam em scripts/install; no pacote já vêm na raiz.
  if [ -d "$ORIGEM/scripts/install" ]; then
    cp "$ORIGEM/scripts/install/instalar.sh" "$DESTINO/instalar.sh"
    cp "$ORIGEM/scripts/install/desinstalar.sh" "$DESTINO/desinstalar.sh"
  fi
fi
chmod +x "$DESTINO/app/Pitstop" "$DESTINO/app/pit" "$DESTINO/pit" "$DESTINO/instalar.sh" "$DESTINO/desinstalar.sh" 2>/dev/null || true
[ -f "$DESTINO/app/createdump" ] && chmod +x "$DESTINO/app/createdump"

# comandos no ~/.local/bin (a maioria das distros já põe essa pasta no PATH)
mkdir -p "$BIN"
ln -sf "$DESTINO/app/Pitstop" "$BIN/pitstop"
ln -sf "$DESTINO/pit" "$BIN/pit"

# ícone e atalho no menu de aplicativos (freedesktop)
ICONES="$DADOS/icons/hicolor/256x256/apps"
mkdir -p "$ICONES" "$DADOS/applications"
cp "$DESTINO/web/icone.png" "$ICONES/pitstop.png"
cat > "$DADOS/applications/pitstop.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Pitstop
GenericName=Runner local de ambientes de desenvolvimento
Comment=Sobe e para Tomcat, Java, npm e comandos personalizados com um clique
Exec="$DESTINO/app/Pitstop"
Icon=pitstop
Terminal=false
Categories=Development;
StartupWMClass=pitstop
Keywords=tomcat;java;maven;npm;command;runner;development;
EOF
chmod +x "$DADOS/applications/pitstop.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DADOS/applications" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q "$DADOS/icons/hicolor" >/dev/null 2>&1 || true

# o que a janela precisa do sistema (bibliotecas gráficas comuns em qualquer desktop)
FALTA=""
for lib in libX11.so.6 libfontconfig.so.1 libICE.so.6 libSM.so.6; do
  ldconfig -p 2>/dev/null | grep -q "$lib" || FALTA="$FALTA $lib"
done
if [ -n "$FALTA" ]; then
  echo "  Aviso: faltam bibliotecas da janela:$FALTA"
  echo "    Debian/Ubuntu: sudo apt install libx11-6 libfontconfig1 libice6 libsm6"
  echo "    Fedora:        sudo dnf install libX11 fontconfig libICE libSM"
  echo "  (o terminal funciona sem elas: pit ui abre a mesma tela no navegador)"
fi

VERSAO=$(basename "$ORIGEM" | sed -n 's/^pitstop-\([0-9.]*\)-.*/\1/p')
printf '\n  \033[32mPitstop %s instalado em %s\033[0m\n' "${VERSAO:-}" "$DESTINO"
echo "  Comandos: pitstop (janela + bandeja) e pit (terminal). Menu de aplicativos: Pitstop."
case ":$PATH:" in *":$BIN:"*) ;; *) echo "  Ponha $BIN no PATH para usar os comandos (ex.: no ~/.profile: export PATH=\"\$HOME/.local/bin:\$PATH\")" ;; esac
echo "  Na primeira abertura um assistente pergunta os caminhos padrão (JDK, Tomcat, Maven) - tudo opcional."
echo "  JDK, Tomcat, Maven e Node são gratuitos: adoptium.net, tomcat.apache.org, maven.apache.org, nodejs.org"
echo "  GNOME: o ícone da bandeja precisa da extensão AppIndicator; sem ela, abra pelo menu de aplicativos."
echo
if [ "$NAO_ABRIR" = 0 ] && [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && pergunta "  Abrir o Pitstop agora?" s; then
  nohup "$DESTINO/app/Pitstop" >/dev/null 2>&1 &
fi
