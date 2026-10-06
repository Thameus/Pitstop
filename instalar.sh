#!/bin/sh
# Wrapper para instalar a partir do repositório. Os pacotes de release recebem o instalador real na raiz.
set -eu
RAIZ=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PITSTOP_SOURCE_ROOT="$RAIZ" exec sh "$RAIZ/scripts/install/instalar.sh" "$@"
