#!/bin/sh
# Pitstop Linux self-extracting installer. O payload .tar.xz é anexado por scripts/release/empacotar.ps1.
set -eu

VERSION="@VERSION@"
SELF="$0"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA_HOME/pitstop"
TMP="${TMPDIR:-/tmp}/pitstop-setup-$$"
mkdir -p "$TMP"
trap 'rm -rf "$TMP"' EXIT INT TERM

ask() {
  text="$1"; def="$2"
  if [ "$def" = s ]; then opt='[S/n]'; else opt='[s/N]'; fi
  printf '%s %s ' "$text" "$opt"
  read -r ans || ans=''
  [ -z "$ans" ] && ans="$def"
  case "$ans" in s|S|sim|y|Y|yes) return 0 ;; *) return 1 ;; esac
}
download() {
  url="$1"; out="$2"
  echo "  Baixando $url"
  if command -v curl >/dev/null 2>&1; then curl -fL --retry 3 -o "$out" "$url"
  elif command -v wget >/dev/null 2>&1; then wget -O "$out" "$url"
  else echo "curl ou wget é necessário para baixar ferramentas opcionais." >&2; return 1
  fi
}
download_text() {
  url="$1"
  if command -v curl >/dev/null 2>&1; then curl -fsL --retry 3 "$url"
  elif command -v wget >/dev/null 2>&1; then wget -qO- "$url"
  else return 1
  fi
}
hash_file() {
  alg="$1"; file="$2"
  if command -v "${alg}sum" >/dev/null 2>&1; then "${alg}sum" "$file" | awk '{print $1}'
  elif command -v openssl >/dev/null 2>&1; then openssl dgst "-$alg" "$file" | awk '{print $NF}'
  else echo "é necessário ${alg}sum ou openssl para verificar downloads." >&2; return 1
  fi
}
verify_hash() {
  alg="$1"; file="$2"; expected="$3"; label="$4"
  [ -n "$expected" ] || { echo "checksum do $label não encontrado." >&2; exit 1; }
  actual=$(hash_file "$alg" "$file") || exit 1
  [ "$(printf '%s' "$expected" | tr 'A-F' 'a-f')" = "$(printf '%s' "$actual" | tr 'A-F' 'a-f')" ] ||
    { echo "checksum do $label não confere." >&2; exit 1; }
}
set_env() {
  key="$1"; val="$2"
  [ -n "$val" ] || return 0
  envf="$DEST/.env"
  [ -f "$envf" ] || { [ -f "$DEST/.env.exemplo" ] && cp "$DEST/.env.exemplo" "$envf" || : > "$envf"; }
  outf="$TMP/env.$$"
  awk -v k="$key" -v v="$val" '
    BEGIN { found=0 }
    $0 ~ "^[[:space:]]*#?[[:space:]]*" k "[[:space:]]*=" {
      if (!found) { print k "=" v; found=1 }
      next
    }
    { print }
    END { if (!found) print k "=" v }
  ' "$envf" > "$outf"
  mv "$outf" "$envf"
}
extract_one() {
  archive="$1"; target="$2"; mode="$3"
  work="$TMP/extract-$$"
  rm -rf "$work"; mkdir -p "$work"
  case "$mode" in
    gz) tar -xzf "$archive" -C "$work" ;;
    xz) tar -xJf "$archive" -C "$work" ;;
  esac
  first=$(find "$work" -mindepth 1 -maxdepth 1 -type d | head -n 1)
  [ -n "$first" ] || { echo "arquivo sem pasta raiz: $archive" >&2; return 1; }
  rm -rf "$target"
  mkdir -p "$(dirname "$target")"
  mv "$first" "$target"
}

command -v tar >/dev/null 2>&1 || { echo "tar não encontrado."; exit 1; }
payload_line=$(awk '/^__PITSTOP_PAYLOAD_BELOW__$/ { print NR + 1; exit }' "$SELF")
[ -n "$payload_line" ] || { echo "payload do instalador não encontrado."; exit 1; }
tail -n +"$payload_line" "$SELF" | tar -xJf - -C "$TMP"
PKG=$(find "$TMP" -mindepth 1 -maxdepth 1 -type d -name 'pitstop-*-linux-x64' | head -n 1)
[ -n "$PKG" ] || { echo "pacote do Pitstop não encontrado dentro do instalador."; exit 1; }

if [ "${1:-}" = "--smoke" ]; then
  PIT_RAIZ="$PKG" PIT_SEM_AUTOSTART=1 "$PKG/app/pit" status
  exit $?
fi

if [ "${1:-}" = "--update" ]; then
  [ -n "${2:-}" ] || { echo "uso: $SELF --update <destino>" >&2; exit 2; }
  DEST="$2"
  echo "  Atualizando o Pitstop em $DEST ..."
  sh "$PKG/instalar.sh" "$DEST" --sim --nao-abrir
  echo "  Pitstop $VERSION atualizado."
  exit 0
fi

printf '\n  Pitstop %s - instalação Linux\n\n' "$VERSION"
printf '  Pasta de instalação [%s]: ' "$DEST"
read -r answer || answer=''
[ -n "$answer" ] && DEST="$answer"

echo "  Instalando o Pitstop..."
sh "$PKG/instalar.sh" "$DEST" --sim --nao-abrir
TOOLS="$DEST/tools"
mkdir -p "$TOOLS"
JDK_HOME="${JAVA_HOME:-}"
MAVEN_HOME="${MAVEN_HOME:-${M2_HOME:-}}"
TOMCAT_HOME="${CATALINA_HOME:-}"
NODE_HOME="${NODE_HOME:-}"
if [ -z "$NODE_HOME" ] && command -v node >/dev/null 2>&1; then
  NODE_HOME=$(dirname "$(command -v node)")
fi

if [ -n "$JDK_HOME" ] && [ -x "$JDK_HOME/bin/java" ]; then
  ask "  JDK detectado em $JDK_HOME. Usar como padrão?" s || JDK_HOME=''
fi
if [ -z "$JDK_HOME" ] && ask "  Baixar Eclipse Temurin JDK 21 portátil?" n; then
  jdk="$TMP/jdk.tar.gz"
  jdk_api='https://api.adoptium.net/v3/binary/latest/21/ga/linux/x64/jdk/hotspot/normal/eclipse?project=jdk'
  if command -v curl >/dev/null 2>&1; then
    jdk_url=$(curl -fsS --retry 3 -o /dev/null -w '%{redirect_url}' "$jdk_api")
  elif command -v wget >/dev/null 2>&1; then
    jdk_url=$(wget -qS --spider "$jdk_api" 2>&1 | awk '/^  Location:/ { print $2; exit }' | tr -d '\r')
  else
    echo "curl ou wget é necessário para baixar ferramentas opcionais." >&2
    exit 1
  fi
  [ -n "$jdk_url" ] || { echo 'não foi possível descobrir a URL do Eclipse Temurin.' >&2; exit 1; }
  download "$jdk_url" "$jdk"
  expected=$(download_text "$jdk_url.sha256.txt" | grep -Eo '[0-9a-fA-F]{64}' | head -n 1)
  verify_hash sha256 "$jdk" "$expected" 'Eclipse Temurin'
  JDK_HOME="$TOOLS/jdk-21"
  extract_one "$jdk" "$JDK_HOME" gz
fi

if [ -n "$MAVEN_HOME" ] && [ -x "$MAVEN_HOME/bin/mvn" ]; then
  ask "  Maven detectado em $MAVEN_HOME. Usar como padrão?" s || MAVEN_HOME=''
fi
if [ -z "$MAVEN_HOME" ] && ask "  Baixar Apache Maven 3.10.0 portátil?" n; then
  mvn="$TMP/maven.tar.gz"
  mvn_url='https://dlcdn.apache.org/maven/maven-3/3.10.0/binaries/apache-maven-3.10.0-bin.tar.gz'
  download "$mvn_url" "$mvn"
  expected=$(download_text "$mvn_url.sha512" | grep -Eo '[0-9a-fA-F]{128}' | head -n 1)
  verify_hash sha512 "$mvn" "$expected" Maven
  MAVEN_HOME="$TOOLS/maven"
  extract_one "$mvn" "$MAVEN_HOME" gz
fi

if [ -n "$TOMCAT_HOME" ] && [ -x "$TOMCAT_HOME/bin/catalina.sh" ]; then
  ask "  Tomcat detectado em $TOMCAT_HOME. Usar como padrão?" s || TOMCAT_HOME=''
fi
if [ -z "$TOMCAT_HOME" ]; then
  printf '  Baixar Tomcat portátil? 0=não, 9=9.0.122, 10=10.1.60, 11=11.0.26 [0]: '
  read -r tc || tc=''
  [ -z "$tc" ] && tc=0
  case "$tc" in
    9)  tver='9.0.122'; tmajor='9' ;;
    10) tver='10.1.60'; tmajor='10' ;;
    11) tver='11.0.26'; tmajor='11' ;;
    *) tver=''; tmajor='' ;;
  esac
  if [ -n "$tver" ]; then
    tfile="$TMP/tomcat.tar.gz"
    turl="https://dlcdn.apache.org/tomcat/tomcat-$tmajor/v$tver/bin/apache-tomcat-$tver.tar.gz"
    download "$turl" "$tfile"
    expected=$(download_text "$turl.sha512" | grep -Eo '[0-9a-fA-F]{128}' | head -n 1)
    verify_hash sha512 "$tfile" "$expected" Tomcat
    TOMCAT_HOME="$TOOLS/tomcat-$tmajor"
    extract_one "$tfile" "$TOMCAT_HOME" gz
  fi
fi
if [ -n "$NODE_HOME" ] && { [ -x "$NODE_HOME/node" ] || [ -x "$NODE_HOME/bin/node" ]; }; then
  ask "  Node.js detectado em $NODE_HOME. Usar como padrão?" s || NODE_HOME=''
fi
if [ -z "$NODE_HOME" ] && ask "  Baixar Node.js LTS portátil?" n; then
  nver=$(download_text 'https://nodejs.org/dist/index.tab' | awk -F '\t' 'NR > 1 && $10 != "-" { print $1; exit }')
  [ -n "$nver" ] || { echo 'não foi possível descobrir a versão LTS do Node.js.' >&2; exit 1; }
  nname="node-$nver-linux-x64.tar.xz"
  nurl="https://nodejs.org/dist/$nver/$nname"
  nfile="$TMP/node.tar.xz"
  download "$nurl" "$nfile"
  expected=$(download_text "https://nodejs.org/dist/$nver/SHASUMS256.txt" | awk -v f="$nname" '$2 == f { print $1; exit }')
  verify_hash sha256 "$nfile" "$expected" Node.js
  NODE_HOME="$TOOLS/node"
  extract_one "$nfile" "$NODE_HOME" xz
fi

printf '  Pasta dos projetos (opcional; Enter para configurar depois): '
read -r PROJECTS || PROJECTS=''
if [ -n "$PROJECTS" ] && [ ! -d "$PROJECTS" ]; then
  echo "  A pasta não existe; será ignorada: $PROJECTS"
  PROJECTS=''
fi

set_env JDK_HOME "$JDK_HOME"
set_env MAVEN_HOME "$MAVEN_HOME"
set_env TOMCAT_HOME "$TOMCAT_HOME"
set_env NODE_HOME "$NODE_HOME"
set_env PROJETOS_DIR "$PROJECTS"

echo
echo "  Pitstop $VERSION instalado em $DEST"
[ -n "$JDK_HOME" ] && echo "  JDK: $JDK_HOME"
[ -n "$MAVEN_HOME" ] && echo "  Maven: $MAVEN_HOME"
[ -n "$TOMCAT_HOME" ] && echo "  Tomcat: $TOMCAT_HOME"
[ -n "$NODE_HOME" ] && echo "  Node.js: $NODE_HOME"
echo
if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && ask "  Abrir o Pitstop agora?" s; then
  nohup "$DEST/app/Pitstop" >/dev/null 2>&1 &
fi
exit 0

__PITSTOP_PAYLOAD_BELOW__
