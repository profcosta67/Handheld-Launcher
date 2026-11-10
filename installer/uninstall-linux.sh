#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Handheld Launcher — desinstalador Linux.
#
#   ./uninstall-linux.sh           # remove a app, atalhos, ícone e auto-arranque
#   ./uninstall-linux.sh --purge   # remove TAMBÉM ~/.config/HandheldLauncher
#                                  # (settings.json, library.json, saves, capas)
#   ./uninstall-linux.sh --dry-run
#
# Preserva por omissão os teus dados (~/.config/HandheldLauncher): a API key do
# SteamGridDB, a biblioteca e as capas continuam lá se reinstalares.
# ---------------------------------------------------------------------------

if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi

set -euo pipefail

APP_NAME="HandheldLauncher"
APP_ID="handheld-launcher"
APP_TITLE="Handheld Launcher"

if [ -f "$0" ] && grep -q $'\r' "$0" 2>/dev/null; then
  _tmp="$(mktemp)"
  tr -d '\r' < "$0" > "$_tmp"
  chmod +x "$_tmp"
  exec bash "$_tmp" "$@"
fi

INSTALL_DIR="$HOME/.local/share/handheld-launcher"
BIN_DIR="$HOME/.local/bin"
APPS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
AUTOSTART_DIR="$HOME/.config/autostart"
CONFIG_DIR="$HOME/.config/HandheldLauncher"
STATE_DIR="$HOME/.local/state/handheld-launcher"
DESKTOP_ID="$APP_ID.desktop"

PURGE=0
DRY_RUN=0

while [ "$#" -gt 0 ]; do
  case "$1" in
    --purge)   PURGE=1 ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help)
      cat <<EOF
$APP_TITLE — desinstalador Linux

Uso: ./uninstall-linux.sh [opções]

  --purge     Apaga também os dados do utilizador ($CONFIG_DIR).
  --dry-run   Mostra o que faria, sem apagar nada.
  -h, --help  Esta ajuda.
EOF
      exit 0
      ;;
    *) echo "Opção desconhecida: $1 (usa --help)." >&2; exit 1 ;;
  esac
  shift
done

if [ "$(id -u)" = "0" ]; then
  echo "!! Não corras isto como root/sudo: os ficheiros estão em \$HOME do utilizador." >&2
  exit 1
fi

do_rm() {  # $1 = caminho
  if [ ! -e "$1" ]; then
    return 0
  fi
  if [ "$DRY_RUN" = "1" ]; then
    echo "   [dry-run] rm -rf $1"
  else
    rm -rf -- "$1"
    echo "   removido: $1"
  fi
}

echo "== $APP_TITLE — desinstalador =="

# 1. Fechar o launcher se estiver aberto (melhor esforço).
if command -v pkill >/dev/null 2>&1; then
  pkill -f "$INSTALL_DIR/$APP_NAME" >/dev/null 2>&1 || true
fi

# 2. Atalhos/entradas
do_rm "$AUTOSTART_DIR/$DESKTOP_ID"
do_rm "$APPS_DIR/$DESKTOP_ID"
do_rm "$ICONS_DIR/$APP_ID.png"
do_rm "$BIN_DIR/handheld-launcher"
# Atalho no ambiente de trabalho (beta.7): o xdg-user-dir resolve
# "Área de Trabalho"/"Ambiente de Trabalho"; o fallback cobre o resto.
for d in "$(command -v xdg-user-dir >/dev/null 2>&1 && xdg-user-dir DESKTOP 2>/dev/null || true)" \
         "$HOME/Desktop" "$HOME/Área de Trabalho" "$HOME/Ambiente de Trabalho"; do
  if [ -n "$d" ] && [ "$d" != "$HOME" ] && [ -f "$d/$DESKTOP_ID" ]; then
    do_rm "$d/$DESKTOP_ID"
  fi
done

# 3. Aplicação
do_rm "$INSTALL_DIR"

# 4. Atualizar caches (se as ferramentas existirem)
if [ "$DRY_RUN" = "0" ]; then
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true
  fi
fi

# 5. Dados do utilizador (só com --purge)
if [ "$PURGE" = "1" ]; then
  do_rm "$CONFIG_DIR"
  do_rm "$STATE_DIR"
else
  if [ -d "$CONFIG_DIR" ]; then
    echo "   (mantidos) dados do utilizador: $CONFIG_DIR"
    echo "   → usa --purge para apagar também settings.json, biblioteca e capas."
  fi
fi

echo ""
echo "== Removido. =="
echo "   Nota: o bloco que o instalador acrescentou ao ~/.profile (PATH com"
echo "   ~/.local/bin) foi deixado intacto — é inofensivo e reutilizável."
