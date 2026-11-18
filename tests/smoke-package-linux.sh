#!/bin/sh
set -eu

DIST="${1:-dist}"
VERSION=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' src/Directory.Build.props | head -n 1)
[ -n "$VERSION" ] || { echo "version not found"; exit 1; }

ARCHIVE="$DIST/pitstop-$VERSION-linux-x64.tar.xz"
RUN="$DIST/pitstop-$VERSION-linux-x64.run"
[ -f "$ARCHIVE" ] || { echo "package not found: $ARCHIVE"; exit 1; }
[ -f "$RUN" ] || { echo "installer not found: $RUN"; exit 1; }

sh "$RUN" --smoke

TMP=$(mktemp -d)
PID=""
cleanup() {
  if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then kill "$PID" 2>/dev/null || true; fi
  rm -rf "$TMP"
}
trap cleanup EXIT INT TERM

tar -xJf "$ARCHIVE" -C "$TMP"
ROOT="$TMP/pitstop-$VERSION-linux-x64"
export PIT_RAIZ="$ROOT"
export PIT_SEM_AUTOSTART=1
export RUNNER_ABRIR_NAVEGADOR=false

"$ROOT/app/pit" status
[ -f "$ROOT/config/perfis.json" ] || { echo "config/perfis.json was not created"; exit 1; }

# Atualização Linux: destino pai vira <pai>/pitstop; pitstop/Pitstop e instalação existente (app/Pitstop) são usados como estão.
mkdir -p "$TMP/home" "$TMP/xdg"
for MODE in parent lower upper existing; do
  if [ "$MODE" = parent ]; then
    ARG_ROOT="$TMP/update-parent"
    UPDATE_ROOT="$ARG_ROOT/pitstop"
  elif [ "$MODE" = lower ]; then
    ARG_ROOT="$TMP/update-lower/pitstop"
    UPDATE_ROOT="$ARG_ROOT"
  elif [ "$MODE" = upper ]; then
    ARG_ROOT="$TMP/update-upper/Pitstop"
    UPDATE_ROOT="$ARG_ROOT"
  else
    ARG_ROOT="$TMP/update-existing/MeuPitstopApp"
    UPDATE_ROOT="$ARG_ROOT"
  fi
  mkdir -p "$UPDATE_ROOT/app" "$UPDATE_ROOT/config"
  printf '%s\n' old > "$UPDATE_ROOT/app/old.txt"
  if [ "$MODE" = existing ]; then : > "$UPDATE_ROOT/app/Pitstop"; fi
  printf '%s\n' 'JDK_HOME=/tmp/jdk-test' > "$UPDATE_ROOT/.env"
  printf '%s\n' '{"perfis":{"preservar":{"tipo":"comando","comando":"echo ok"}}}' > "$UPDATE_ROOT/config/perfis.json"
  HOME="$TMP/home" XDG_DATA_HOME="$TMP/xdg" sh "$RUN" --update "$ARG_ROOT"
  [ -x "$UPDATE_ROOT/app/Pitstop" ] || { echo "Linux update did not install Pitstop ($MODE)"; exit 1; }
  [ ! -f "$UPDATE_ROOT/app/old.txt" ] || { echo "Linux update left old app file ($MODE)"; exit 1; }
  [ ! -d "$UPDATE_ROOT/pitstop" ] && [ ! -d "$UPDATE_ROOT/Pitstop" ] || { echo "Linux update duplicated Pitstop root ($MODE)"; exit 1; }
  grep -q '^JDK_HOME=/tmp/jdk-test$' "$UPDATE_ROOT/.env" || { echo "Linux update changed .env ($MODE)"; exit 1; }
  grep -q '"preservar"' "$UPDATE_ROOT/config/perfis.json" || { echo "Linux update changed perfis.json ($MODE)"; exit 1; }
  [ -f "$UPDATE_ROOT/THIRD-PARTY-NOTICES.md" ] || { echo "Linux install omitted root third-party notices ($MODE)"; exit 1; }
  [ -f "$UPDATE_ROOT/third-party/VERSIONS.json" ] || { echo "Linux install omitted third-party directory ($MODE)"; exit 1; }
done

PORT=19999
"$ROOT/app/pit" ui "$PORT" >"$TMP/ui.log" 2>&1 &
PID=$!

READY=0
i=0
while [ "$i" -lt 40 ]; do
  if ! kill -0 "$PID" 2>/dev/null; then
    cat "$TMP/ui.log"
    echo "pit ui exited early"
    exit 1
  fi
  CODE=$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/api/status" 2>/dev/null || true)
  if [ "$CODE" = "200" ]; then READY=1; break; fi
  i=$((i + 1))
  sleep 0.25
done
[ "$READY" = 1 ] || { cat "$TMP/ui.log"; echo "HTTP server did not become ready"; exit 1; }

CODE=$(curl -sS -o /dev/null -w '%{http_code}' -H 'Host: example.invalid' "http://127.0.0.1:$PORT/api/status")
[ "$CODE" = "403" ] || { echo "invalid Host returned $CODE"; exit 1; }

CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X PUT -H 'Content-Type: application/json' --data '{"perfis":{}}' "http://127.0.0.1:$PORT/api/cfg")
[ "$CODE" = "403" ] || { echo "PUT without X-PIT returned $CODE"; exit 1; }

CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X POST -H 'X-PIT: 1' "http://127.0.0.1:$PORT/api/sair?rapido=1")
[ "$CODE" = "200" ] || { echo "POST /api/sair returned $CODE"; exit 1; }

wait "$PID"
RC=$?
PID=""
[ "$RC" = 0 ] || { cat "$TMP/ui.log"; echo "pit ui exited with $RC"; exit 1; }

echo "LINUX PACKAGE SMOKE OK: self-extracting installer, update preservation, self-contained CLI, HTTP protections and clean shutdown."
