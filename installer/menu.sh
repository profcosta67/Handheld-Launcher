#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Handheld Launcher — instalador "de duplo-clique" para Linux
# (o equivalente Linux ao .bat do Windows).
#
#   ./instalador/menu.sh                  # abre o menu
#   ./instalador/menu.sh --sem-perguntas  # instala + arranque no login
#   ./instalador/menu.sh --no-autostart   # instala sem arranque no login
#   ./instalador/menu.sh --desinstalar    # remove tudo
#
# Este script NÃO tem lógica de instalação: mostra o menu e delega no
# install-linux.sh (que faz a cópia, o atalho, o menu de aplicações, o ícone,
# o PATH, o ldd e o log em ~/.local/state/handheld-launcher/install.log).
#
# Layout do pacote (0.24.3-beta.7):
#   handheld-launcher/
#     INSTALAR.sh           ← atalho de duplo-clique que abre este menu
#     instalador/           ← este script + install-linux.sh + uninstall-linux.sh
#     arquivos/             ← binário e tudo o que ele precisa (+ version.txt)
#
# NUNCA correr com sudo: a instalação é por utilizador (~/.local).
# ---------------------------------------------------------------------------

if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi

# Sem `set -e`: aqui queremos tratar as falhas (mostrar o log e devolver o
# código de saída do instalador), não abortar a meio do menu.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
INSTALLER="$HERE/install-linux.sh"
UNINSTALLER="$HERE/uninstall-linux.sh"

# Auto-correção de CRLF, igual aos outros scripts (zip feito no Windows).
if [ -f "$0" ] && grep -q $'\r' "$0" 2>/dev/null; then
  echo ">> Aviso: finais de linha Windows (CRLF) neste script — a corrigir..."
  _tmp="$(mktemp)"
  tr -d '\r' < "$0" > "$_tmp"
  chmod +x "$_tmp"
  HANDHELD_INSTALLER_HERE="$HERE" exec bash "$_tmp" "$@"
fi

VERSION_LABEL=""
if [ -f "$HERE/../arquivos/version.txt" ]; then
  VERSION_LABEL="$(tr -d ' \r\n' < "$HERE/../arquivos/version.txt")"
fi

PULAR_MENU=0
ACAO="instalar"        # instalar | desinstalar
AUTOSTART_FORCADO=""   # "" = decidir no menu | 1 = ligar | 0 = desligar
EXTRA=()

usage() {
  cat <<'EOF'
Handheld Launcher — instalador Linux (duplo-clique)

Uso: ./instalador/menu.sh [opções]

  (sem opções)        Abre o menu: instalar, instalar sem arranque no login
                      ou desinstalar.
  --sem-perguntas     Não pergunta nada: instala com arranque automático no
                      login (é o que o CI usa).
  --autostart         Força o arranque automático no login.
  --no-autostart      Instala sem arranque automático (remove um anterior).
  --desinstalar       Remove a aplicação, o atalho, o menu, o ícone, o
                      arranque automático e o atalho do ambiente de trabalho.
  --purge             Com --desinstalar: apaga também os dados do utilizador.
  -h, --help          Esta ajuda.

Qualquer outra opção é passada ao install-linux.sh
(ex.: --no-add-path, --prefix DIR, --dry-run).

O log de cada instalação fica em ~/.local/state/handheld-launcher/install.log
EOF
}

for arg in "$@"; do
  case "$arg" in
    --sem-perguntas) PULAR_MENU=1 ;;
    --autostart)     AUTOSTART_FORCADO=1 ;;
    --no-autostart)  AUTOSTART_FORCADO=0 ;;
    --desinstalar)   ACAO="desinstalar" ;;
    -h|--help)       usage; exit 0 ;;
  esac
done

# Separa o que é nosso do que vai para o install/uninstall-linux.sh.
DESEJO_PURGE=0
while [ "$#" -gt 0 ]; do
  case "$1" in
    --sem-perguntas|--autostart|--no-autostart|--desinstalar) ;;
    --purge) DESEJO_PURGE=1 ;;
    *) EXTRA+=("$1") ;;
  esac
  shift
done

pausar() {
  # Só pausa em modo interactivo real (duplo-clique). Em --sem-perguntas ou com
  # stdin não-TTY (CI) nunca bloqueia à espera de um Enter que não vem.
  if [ "$PULAR_MENU" = "0" ] && [ -t 0 ]; then
    printf '\nPrima Enter para fechar esta janela...'
    read -r _ || true
  fi
}

mostrar_falha() {
  local log="$HOME/.local/state/handheld-launcher/install.log"
  printf '\n!! Não foi possível concluir.\n'
  if [ -f "$log" ]; then
    printf -- '--- últimas linhas de %s ---\n' "$log"
    tail -n 20 "$log" || true
    printf -- '-------------------------------\n'
  fi
  printf 'Reporta em Central de Ajuda → Reportar erro, colando esse log.\n'
}

if [ ! -f "$INSTALLER" ]; then
  printf '!! Não encontrei o install-linux.sh em %s\n' "$HERE" >&2
  printf '   Extrai o pacote completo (a pasta arquivos/ tem de estar ao lado).\n' >&2
  exit 1
fi

printf '\n=============================================\n'
if [ -n "$VERSION_LABEL" ]; then
  printf ' Handheld Launcher %s — instalação Linux\n' "$VERSION_LABEL"
else
  printf ' Handheld Launcher — instalação Linux\n'
fi
printf '=============================================\n'
printf ' (não uses sudo — a instalação é para o teu utilizador)\n\n'

if [ "$ACAO" = "instalar" ] && [ "$PULAR_MENU" = "0" ] && [ -t 0 ]; then
  printf '  1) Instalar e abrir sozinho no login (handheld)   [Enter]\n'
  printf '  2) Instalar sem abrir no login\n'
  printf '  3) Desinstalar\n'
  printf '  0) Sair\n\n'
  read -r -p 'Escolhe [1]: ' ESCOLHA || ESCOLHA=""
  ESCOLHA="${ESCOLHA:-1}"
  case "$ESCOLHA" in
    1) ACAO="instalar" ;;
    2) ACAO="instalar"; AUTOSTART_FORCADO=0 ;;
    3) ACAO="desinstalar" ;;
    0) printf 'Nada foi alterado.\n'; exit 0 ;;
    *) printf 'Opção inválida (%s) — a usar 1.\n' "$ESCOLHA"; ACAO="instalar" ;;
  esac
  printf '\n'
fi

# Sem menu (--sem-perguntas) e sem escolha explícita: assume o modo handheld.
if [ "$ACAO" = "instalar" ] && [ -z "$AUTOSTART_FORCADO" ] && [ "$PULAR_MENU" = "1" ]; then
  AUTOSTART_FORCADO=1
fi

RC=0
if [ "$ACAO" = "desinstalar" ]; then
  if [ ! -f "$UNINSTALLER" ]; then
    printf '!! Não encontrei o uninstall-linux.sh em %s\n' "$HERE" >&2
    exit 1
  fi
  ARGS_DES=()
  [ "$DESEJO_PURGE" = "1" ] && ARGS_DES+=("--purge")
  bash "$UNINSTALLER" ${ARGS_DES[@]+"${ARGS_DES[@]}"} || RC=$?
  [ "$RC" -ne 0 ] && mostrar_falha
  pausar
  exit "$RC"
fi

ARGS=()
case "${AUTOSTART_FORCADO:-}" in
  1) ARGS+=("--autostart") ;;
  0) ARGS+=("--no-autostart") ;;
esac
# Só abre a aplicação no fim quando há menu (janela visível para o utilizador);
# nunca em --sem-perguntas, que é o caminho do CI.
if [ "$PULAR_MENU" = "0" ] && [ "${AUTOSTART_FORCADO:-}" = "1" ] && [ -t 0 ]; then
  ARGS+=("--run")
fi
ARGS+=(${EXTRA[@]+"${EXTRA[@]}"})

bash "$INSTALLER" ${ARGS[@]+"${ARGS[@]}"} || RC=$?

if [ "$RC" -ne 0 ]; then
  mostrar_falha
elif command -v gio >/dev/null 2>&1 && [ -f "$HERE/../INSTALAR.sh" ]; then
  # Depois de correr uma vez, o atalho INSTALAR fica marcado como "confiável"
  # no GNOME: no próximo duplo-clique já não pede autorização.
  gio set "$HERE/../INSTALAR.sh" metadata::trusted true >/dev/null 2>&1 || true
fi

pausar
exit "$RC"
