#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Handheld Launcher — instalador Linux (alvo principal: Bazzite; testado também
# em Zorin/Ubuntu e noutras distros com bash).
#
#   tar -xzf HandheldLauncher-linux-x64.tar.gz
#   cd handheld-launcher
#   ./install-linux.sh                # instala em ~/.local (NUNCA com sudo)
#   ./install-linux.sh --autostart    # + arranca no login (modo handheld)
#   ./install-linux.sh --run          # instala e abre o launcher no fim
#   ./install-linux.sh --uninstall    # remove tudo o que foi instalado
#   ./install-linux.sh --help
#
# Porque tudo isto existe (v0.24.3-beta.5): na beta.4 o script copiava os
# ficheiros e dizia "Pronto", mas o atalho ficava em ~/.local/bin — fora do
# PATH da sessão atual — e não havia entrada no menu de aplicações. O
# utilizador via "handheld-launcher: comando não encontrado" e concluía que
# nada tinha sido instalado (relatado no Zorin). Agora há entrada de menu com
# ícone, PATH corrigido, verificação de bibliotecas e log do que aconteceu.
#
# NUNCA corras com sudo: a instalação é por utilizador. Com sudo, $HOME passa
# a /root, tudo é gravado em /root/.local e o launcher "desaparece".
# ---------------------------------------------------------------------------

# 0. Corre sempre em bash, mesmo que alguém chame `sh install-linux.sh`
#    (o `set -o pipefail` e o ${BASH_SOURCE} abaixo não existem em dash).
if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi

set -euo pipefail

APP_NAME="HandheldLauncher"
APP_ID="handheld-launcher"
APP_TITLE="Handheld Launcher"
HERE="${HANDHELD_INSTALLER_HERE:-$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)}"

# 1. Auto-correção de CRLF: se o script veio de um zip feito no Windows,
#    reescreve-se em LF e recomeça (a pasta de origem viaja em env var).
if [ -f "$0" ] && grep -q $'\r' "$0" 2>/dev/null; then
  echo ">> Aviso: finais de linha Windows (CRLF) neste script — a corrigir e a recomeçar..."
  _tmp="$(mktemp)"
  tr -d '\r' < "$0" > "$_tmp"
  chmod +x "$_tmp"
  export HANDHELD_INSTALLER_HERE="$HERE"
  exec bash "$_tmp" "$@"
fi

# Layout do pacote 0.24.3-beta.7: a raiz só tem o INSTALAR.desktop; os binários
# vivem em arquivos/ e os scripts em instalador/. Um pacote antigo (script ao
# lado dos binários) continua a funcionar — daí o fallback para $HERE.
if [ -d "$HERE/../arquivos" ]; then
  SRC_DIR="$(cd "$HERE/../arquivos" && pwd)"
else
  SRC_DIR="$HERE"
fi

INSTALL_DIR="$HOME/.local/share/handheld-launcher"
BIN_DIR="$HOME/.local/bin"
APPS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
AUTOSTART_DIR="$HOME/.config/autostart"
STATE_DIR="$HOME/.local/state/handheld-launcher"
LOG_FILE="$STATE_DIR/install.log"
DESKTOP_ID="$APP_ID.desktop"

MARK_BEGIN="# >>> handheld-launcher (install-linux.sh) >>>"
MARK_END="# <<< handheld-launcher (install-linux.sh) <<<"

AUTOSTART=0
ADD_PATH=1
RUN_AFTER=0
DRY_RUN=0
DESKTOP_SHORTCUT=1
DESKTOP_DIR=""      # resolvido depois dos argumentos (xdg-user-dir DESKTOP)

# ---------------------------------------------------------------------------
# Saída + log: tudo o que aparece no terminal fica também em install.log, para
# o utilizador poder colar no reporte sem repetir comandos.
# ---------------------------------------------------------------------------
mkdir -p "$STATE_DIR" 2>/dev/null || true
: > "$LOG_FILE" 2>/dev/null || true

say()  { printf '%s\n' "$*" | tee -a "$LOG_FILE"; }
warn() { printf '   ! %s\n' "$*" | tee -a "$LOG_FILE" >&2; }
die()  { printf '!! %s\n' "$*" | tee -a "$LOG_FILE" >&2; exit 1; }

on_error() {
  local rc=$? line="$1"
  printf '!! Falhou na linha %s (código %s).\n' "$line" "$rc" | tee -a "$LOG_FILE" >&2
  printf '   Log completo: %s\n' "$LOG_FILE" | tee -a "$LOG_FILE" >&2
  printf '   Reporta em Central de Ajuda → Reportar erro, colando esse log.\n' | tee -a "$LOG_FILE" >&2
  exit "$rc"
}
trap 'on_error "$LINENO"' ERR

run() {
  if [ "$DRY_RUN" = "1" ]; then
    say "   [dry-run] $*"
  else
    "$@"
  fi
}

usage() {
  cat <<EOF
$APP_TITLE — instalador Linux ($APP_ID)

Uso: ./install-linux.sh [opções]

  --autostart        Instala também o arranque automático no login (modo
                     handheld). No PC/desktop isto NÃO é recomendado.
  --no-autostart     Não instala o arranque automático (comportamento padrão).
  --desktop-shortcut Cria também um atalho no ambiente de trabalho
                     (~/Desktop ou ~/Área de Trabalho) — comportamento padrão.
  --no-desktop-shortcut  Não cria o atalho no ambiente de trabalho (e remove
                     um que exista de uma instalação anterior).
  --no-add-path      Não mexe no ~/.profile. O atalho em ~/.local/bin continua
                     a ser criado; usa o caminho completo ou faz logout/login.
  --prefix DIR       Instala em DIR/.local/... em vez de \$HOME.
  --run              Arranca o launcher no fim, se a instalação validar.
  --uninstall        Remove binário, atalho, entrada de menu, ícone e arranque
                     automático (delega no uninstall-linux.sh).
  --dry-run          Mostra o que faria, sem alterar nada.
  -h, --help         Esta ajuda.

Destino (por omissão): $INSTALL_DIR

Depois de instalar, procura "Handheld Launcher" no menu de aplicações, ou
corre 'handheld-launcher' num terminal novo. Para usar já nesta sessão:
  export PATH="\$HOME/.local/bin:\$PATH"

Log desta execução: $LOG_FILE
EOF
}

# ---------------------------------------------------------------------------
# Argumentos
# ---------------------------------------------------------------------------
while [ "$#" -gt 0 ]; do
  case "$1" in
    --autostart)    AUTOSTART=1 ;;
    --no-autostart) AUTOSTART=0 ;;
    --desktop-shortcut)    DESKTOP_SHORTCUT=1 ;;
    --no-desktop-shortcut) DESKTOP_SHORTCUT=0 ;;
    --add-path)     ADD_PATH=1 ;;
    --no-add-path)  ADD_PATH=0 ;;
    --run)          RUN_AFTER=1 ;;
    --dry-run)      DRY_RUN=1 ;;
    --uninstall)
      if [ -f "$HERE/uninstall-linux.sh" ]; then
        exec bash "$HERE/uninstall-linux.sh" "${@:2}"
      fi
      die "Não encontrei '$HERE/uninstall-linux.sh'. Remove à mão:
   rm -rf '$INSTALL_DIR' '$BIN_DIR/handheld-launcher' '$APPS_DIR/$DESKTOP_ID' '$AUTOSTART_DIR/$DESKTOP_ID' '$DESKTOP_DIR/$DESKTOP_ID' '$ICONS_DIR/$APP_ID.png'"
      ;;
    --prefix)
      [ "$#" -ge 2 ] || die "--prefix precisa de uma pasta."
      HOME="$2"; shift
      INSTALL_DIR="$HOME/.local/share/handheld-launcher"
      BIN_DIR="$HOME/.local/bin"
      APPS_DIR="$HOME/.local/share/applications"
      ICONS_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
      AUTOSTART_DIR="$HOME/.config/autostart"
      STATE_DIR="$HOME/.local/state/handheld-launcher"
      LOG_FILE="$STATE_DIR/install.log"
      mkdir -p "$STATE_DIR" 2>/dev/null || true
      ;;
    -h|--help)      usage; exit 0 ;;
    *)              die "Opção desconhecida: $1 (usa --help)." ;;
  esac
  shift
done

# ---------------------------------------------------------------------------
# 1.1 Pasta do ambiente de trabalho (para o atalho). O xdg-user-dir é o
#     standard e resolve "Área de Trabalho"/"Ambiente de Trabalho"; o fallback
#     cobre distros sem xdg-user-dirs.
# ---------------------------------------------------------------------------
resolve_desktop_dir() {
  local d="" candidate
  if command -v xdg-user-dir >/dev/null 2>&1; then
    d="$(xdg-user-dir DESKTOP 2>/dev/null || true)"
  fi
  if [ -z "$d" ] || [ "$d" = "$HOME" ]; then
    for candidate in "$HOME/Desktop" "$HOME/Área de Trabalho" "$HOME/Ambiente de Trabalho"; do
      if [ -d "$candidate" ]; then d="$candidate"; break; fi
    done
  fi
  [ -n "$d" ] || d="$HOME/Desktop"
  DESKTOP_DIR="$d"
}
resolve_desktop_dir

# ---------------------------------------------------------------------------
# 2. Nunca com root/sudo — a causa nº1 de "instalou mas não aparece".
# ---------------------------------------------------------------------------
if [ "$(id -u)" = "0" ]; then
  if [ -n "${SUDO_USER:-}" ] && [ "${SUDO_USER}" != "root" ]; then
    die "Não corras este instalador com sudo (iria para /root).
   Usa: sudo -u $SUDO_USER ./install-linux.sh"
  fi
  die "Não corras este instalador como root.
   A instalação é por utilizador (~/.local); corre ./install-linux.sh com a tua conta."
fi

# ---------------------------------------------------------------------------
# 3. Validar o pacote extraído
# ---------------------------------------------------------------------------
say "== $APP_TITLE — instalador Linux =="
say ">> Pacote: $SRC_DIR"

SRC_BIN="$SRC_DIR/$APP_NAME"
[ -f "$SRC_BIN" ] || die "Não encontrei '$APP_NAME' em '$SRC_DIR'.
   Extrai o pacote COMPLETO e corre o instalador de dentro da pasta extraída:
     tar -xzf HandheldLauncher-linux-x64.tar.gz && cd handheld-launcher
     ./instalador/instalar.sh        (ou duplo-clique em INSTALAR)"

SRC_SIZE="$(wc -c < "$SRC_BIN" 2>/dev/null || echo 0)"
# O apphost do .NET 8 tem ~70 KB; abaixo disto é extração truncada/zumbi.
if [ "${SRC_SIZE:-0}" -lt 20000 ]; then
  die "'$APP_NAME' parece incompleto ($SRC_SIZE bytes). Volta a extrair o pacote."
fi

if command -v file >/dev/null 2>&1; then
  if ! file -b "$SRC_BIN" | grep -qi 'ELF 64-bit'; then
    die "'$APP_NAME' não é um executável Linux x86-64: $(file -b "$SRC_BIN")"
  fi
fi

VERSION_LABEL="desconhecida"
if [ -f "$SRC_DIR/version.txt" ]; then
  VERSION_LABEL="$(tr -d ' \r\n' < "$SRC_DIR/version.txt")"
fi
say ">> Versão do pacote: $VERSION_LABEL"

# ---------------------------------------------------------------------------
# 4. Ferramentas opcionais (melhor esforço — não falha a instalação)
# ---------------------------------------------------------------------------
pkg_hint() {
  local pkgs="$1"
  if [ -e /run/ostree-booted ]; then
    printf 'rpm-ostree install %s   (no Bazzite estas ferramentas já vêm na imagem)' "$pkgs"
  elif command -v apt-get >/dev/null 2>&1; then
    printf 'sudo apt install %s' "$pkgs"
  elif command -v dnf >/dev/null 2>&1; then
    printf 'sudo dnf install %s' "$pkgs"
  elif command -v pacman >/dev/null 2>&1; then
    printf 'sudo pacman -S %s' "$pkgs"
  elif command -v zypper >/dev/null 2>&1; then
    printf 'sudo zypper install %s' "$pkgs"
  else
    printf 'instala %s com o gestor de pacotes da tua distro' "$pkgs"
  fi
}

say ">> Verificando ferramentas opcionais (cada uma liga uma função do Quick Settings):"
missing_pkgs=""

check_tool() {  # $1 = comando, $2 = função, $3 = pacote (dica)
  if command -v "$1" >/dev/null 2>&1; then
    say "   ok   $1 — $2"
  else
    warn "'$1' não encontrado — $2 fica sem efeito."
    missing_pkgs="$missing_pkgs $3"
  fi
}

check_tool brightnessctl "brilho"            brightnessctl
check_tool ryzenadj      "TDP (só APU AMD)"  ryzenadj
check_tool pactl         "volume"            pulseaudio-utils
check_tool nmcli         "Wi-Fi"             network-manager
check_tool bluetoothctl  "Bluetooth"         bluez
check_tool rfkill        "modo avião"        rfkill
check_tool ffmpeg        "captura/gravação"  ffmpeg
check_tool syncthing     "sincronização"     syncthing
check_tool xdg-open      "lançar jogos"      xdg-utils

if [ -n "$missing_pkgs" ]; then
  missing_pkgs="$(printf '%s' "$missing_pkgs" | sed -e 's/^ *//' -e 's/ *$//')"
  say "   → Instala o que falta com: $(pkg_hint "$missing_pkgs")"
fi

if id -nG 2>/dev/null | tr ' ' '\n' | grep -qx input; then
  say "   ok   grupo 'input' — gamepad via evdev acessível"
else
  warn "não estás no grupo 'input' — o gamepad (evdev) pode não responder."
  say "   → sudo usermod -aG input $(id -un)   (depois faz logout/login)"
fi

# ---------------------------------------------------------------------------
# 5. Copiar a aplicação (idempotente: limpa a versão anterior, preserva as
#    configurações do utilizador em ~/.config/HandheldLauncher)
# ---------------------------------------------------------------------------
say ">> Instalando em $INSTALL_DIR"
run mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$APPS_DIR" "$ICONS_DIR"

if [ "$SRC_DIR" != "$INSTALL_DIR" ]; then
  run rm -rf "$INSTALL_DIR"
  run mkdir -p "$INSTALL_DIR"
  if [ "$DRY_RUN" = "0" ]; then
    # cp -a leva a árvore toda: dlls, *.so, version.txt, ícone.
    cp -a "$SRC_DIR/." "$INSTALL_DIR/"
    # Os scripts de instalação ficam dentro da pasta instalada, para se poder
    # desinstalar depois sem voltar a extrair o pacote (no beta.7+ os scripts
    # vivem em instalador/, não ao lado do binário).
    for s in install-linux.sh uninstall-linux.sh; do
      if [ -f "$HERE/$s" ] && [ "$HERE/$s" != "$INSTALL_DIR/$s" ]; then
        cp -f "$HERE/$s" "$INSTALL_DIR/$s"
      fi
    done
  fi
fi

run chmod 755 "$INSTALL_DIR/$APP_NAME"
for s in install-linux.sh uninstall-linux.sh; do
  if [ -f "$INSTALL_DIR/$s" ]; then
    run chmod 755 "$INSTALL_DIR/$s"
  fi
done
if [ "$DRY_RUN" = "0" ]; then
  printf '%s\n' "$VERSION_LABEL" > "$INSTALL_DIR/version.txt"
fi
say ">> Aplicação copiada ($VERSION_LABEL)."

run ln -sfn "$INSTALL_DIR/$APP_NAME" "$BIN_DIR/handheld-launcher"
say ">> Atalho: $BIN_DIR/handheld-launcher"

# ---------------------------------------------------------------------------
# 6. Ícone + entrada no menu de aplicações (era o que faltava na beta.4: sem
#    isto o launcher não aparece em lado nenhum e parece "não instalado")
# ---------------------------------------------------------------------------
ICON_SRC=""
for candidate in "$SRC_DIR/$APP_ID.png" "$SRC_DIR/$APP_NAME.png" "$SRC_DIR/app-icon.png"; do
  if [ -f "$candidate" ]; then
    ICON_SRC="$candidate"
    break
  fi
done

ICON_LINE=""
if [ -n "$ICON_SRC" ]; then
  run install -Dm644 "$ICON_SRC" "$ICONS_DIR/$APP_ID.png"
  ICON_LINE="Icon=$APP_ID"
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true
  fi
else
  warn "ícone não encontrado no pacote — a entrada de menu fica sem ícone."
fi

write_desktop() {  # $1 = ficheiro de destino, $2 = 1 para auto-arranque
  local dest="$1" auto="${2:-0}"
  {
    printf '%s\n' '[Desktop Entry]'
    printf '%s\n' 'Type=Application'
    printf '%s\n' 'Version=1.0'
    printf 'Name=%s\n' "$APP_TITLE"
    printf '%s\n' 'GenericName=Game Launcher'
    printf '%s\n' 'Comment=Launcher para handhelds (Steam, Epic, GOG, Heroic, Eden)'
    printf 'Exec="%s"\n' "$INSTALL_DIR/$APP_NAME"
    if [ -n "$ICON_LINE" ]; then
      printf '%s\n' "$ICON_LINE"
    fi
    printf '%s\n' 'Terminal=false'
    printf '%s\n' 'Categories=Game;'
    printf '%s\n' 'StartupWMClass=HandheldLauncher'
    printf '%s\n' 'Keywords=games;launcher;handheld;steam;epic;gog;'
    if [ "$auto" = "1" ]; then
      printf '%s\n' 'X-GNOME-Autostart-enabled=true'
      printf '%s\n' 'X-GNOME-Autostart-Delay=5'
      printf '%s\n' 'Hidden=false'
    fi
  } > "$dest"
}

if [ "$DRY_RUN" = "1" ]; then
  say "   [dry-run] escreveria $APPS_DIR/$DESKTOP_ID"
else
  write_desktop "$APPS_DIR/$DESKTOP_ID" 0
fi
say ">> Entrada no menu de aplicações: $APPS_DIR/$DESKTOP_ID"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
fi

if [ "$AUTOSTART" = "1" ]; then
  run mkdir -p "$AUTOSTART_DIR"
  if [ "$DRY_RUN" = "1" ]; then
    say "   [dry-run] escreveria $AUTOSTART_DIR/$DESKTOP_ID"
  else
    write_desktop "$AUTOSTART_DIR/$DESKTOP_ID" 1
  fi
  say ">> Arranque automático no login: LIGADO ($AUTOSTART_DIR/$DESKTOP_ID)"
else
  # beta.4 gravava isto sempre; se sobrou de uma instalação anterior, sai.
  if [ -f "$AUTOSTART_DIR/$DESKTOP_ID" ]; then
    run rm -f "$AUTOSTART_DIR/$DESKTOP_ID"
    say ">> Arranque automático no login: DESLIGADO (removido, como pedido pelo padrão)."
  else
    say ">> Arranque automático no login: DESLIGADO (usa --autostart para ligar)."
  fi
fi

# ---------------------------------------------------------------------------
# 6.1 Atalho no ambiente de trabalho (beta.7). No Windows o Velopack cria um
#     atalho no desktop; no Linux faltava. O GNOME/Nautilus só executa um
#     .desktop marcado como "trusted", daí o `gio set` (ausente → só aviso).
# ---------------------------------------------------------------------------
DESKTOP_PATH="$DESKTOP_DIR/$DESKTOP_ID"
if [ "$DESKTOP_SHORTCUT" = "1" ]; then
  if [ "$DRY_RUN" = "1" ]; then
    say "   [dry-run] escreveria $DESKTOP_PATH"
  elif [ -d "$DESKTOP_DIR" ]; then
    write_desktop "$DESKTOP_PATH" 0
    chmod 755 "$DESKTOP_PATH" 2>/dev/null || true
    if command -v gio >/dev/null 2>&1; then
      gio set "$DESKTOP_PATH" metadata::trusted true >/dev/null 2>&1 || true
    fi
    say ">> Atalho no ambiente de trabalho: $DESKTOP_PATH"
  else
    warn "pasta do ambiente de trabalho não encontrada ($DESKTOP_DIR) — atalho não criado."
  fi
elif [ -f "$DESKTOP_PATH" ]; then
  run rm -f "$DESKTOP_PATH"
  say ">> Atalho no ambiente de trabalho: removido (--no-desktop-shortcut)."
fi

# ---------------------------------------------------------------------------
# 7. PATH — o ~/.local/bin só entra no PATH no login seguinte, e era isto que
#    fazia "handheld-launcher: comando não encontrado" logo após instalar.
# ---------------------------------------------------------------------------
IN_PATH=0
case ":$PATH:" in
  *":$BIN_DIR:"*) IN_PATH=1 ;;
esac

PROFILE="$HOME/.profile"
if [ "$IN_PATH" = "1" ]; then
  say ">> PATH: $BIN_DIR já está no PATH desta sessão."
elif [ "$ADD_PATH" = "0" ]; then
  warn "$BIN_DIR não está no PATH (--no-add-path). Usa: export PATH=\"$BIN_DIR:\$PATH\""
elif grep -qF "$MARK_BEGIN" "$PROFILE" 2>/dev/null; then
  say ">> PATH: bloco já presente em $PROFILE (vale a partir do próximo login)."
elif [ "$DRY_RUN" = "1" ]; then
  say "   [dry-run] acrescentaria $BIN_DIR ao PATH em $PROFILE"
else
  {
    printf '\n%s\n' "$MARK_BEGIN"
    printf '%s\n' 'case ":$PATH:" in'
    printf '%s\n' '  *":$HOME/.local/bin:"*) ;;'
    printf '%s\n' '  *) export PATH="$HOME/.local/bin:$PATH" ;;'
    printf '%s\n' 'esac'
    printf '%s\n' "$MARK_END"
  } >> "$PROFILE"
  say ">> PATH: $BIN_DIR acrescentado em $PROFILE (vale no próximo login)."
fi

# ---------------------------------------------------------------------------
# 8. Bibliotecas nativas: sem consola, uma lib em falta só se traduz em "não
#    abre". Aqui o `ldd` diz o nome da lib e o comando para a instalar.
# ---------------------------------------------------------------------------
check_shared_libs() {
  if ! command -v ldd >/dev/null 2>&1; then
    return 0
  fi
  local targets=("$INSTALL_DIR/$APP_NAME") f line name
  local missing=""
  for f in "$INSTALL_DIR"/libAvaloniaNative.so "$INSTALL_DIR"/libSkiaSharp.so "$INSTALL_DIR"/libHarfBuzzSharp.so; do
    if [ -f "$f" ]; then
      targets+=("$f")
    fi
  done
  for f in "${targets[@]}"; do
    while IFS= read -r line; do
      case "$line" in
        *"not found"*)
          name="${line%%=>*}"
          name="$(printf '%s' "$name" | tr -d '\t')"
          missing="$missing $name"
          ;;
      esac
    done <<< "$(ldd "$f" 2>/dev/null || true)"
  done
  missing="$(printf '%s' "$missing" | tr ' ' '\n' | sed '/^$/d' | sort -u | tr '\n' ' ')"
  if [ -z "$missing" ]; then
    say "   ok   bibliotecas do sistema presentes"
    return 0
  fi
  warn "faltam bibliotecas: $missing"
  local pkgs=""
  case "$missing" in *libfontconfig*) pkgs="$pkgs libfontconfig1" ;; esac
  case "$missing" in *libICE*)        pkgs="$pkgs libice6" ;; esac
  case "$missing" in *libSM.*)        pkgs="$pkgs libsm6" ;; esac
  case "$missing" in *libX11*)        pkgs="$pkgs libx11-6" ;; esac
  case "$missing" in *libicu*)        pkgs="$pkgs libicu" ;; esac
  if [ -z "$pkgs" ]; then
    pkgs="$missing"
  fi
  say "   → $(pkg_hint "$pkgs")"
}

say ">> Verificando bibliotecas nativas:"
check_shared_libs

# ---------------------------------------------------------------------------
# 9. Resumo
# ---------------------------------------------------------------------------
if [ "$AUTOSTART" = "1" ]; then
  AUTO_LABEL="ligado"
else
  AUTO_LABEL="desligado"
fi

say ""
say "== Instalação concluída =="
say "   Versão:        $VERSION_LABEL"
say "   Aplicação:     $INSTALL_DIR/$APP_NAME"
say "   Atalho:        $BIN_DIR/handheld-launcher"
say "   Menu:          \"$APP_TITLE\" (categoria Jogos)"
say "   Auto-arranque: $AUTO_LABEL"
DESK_LABEL="não criado"
if [ "$DESKTOP_SHORTCUT" = "1" ] && { [ "$DRY_RUN" = "1" ] || [ -f "$DESKTOP_PATH" ]; }; then
  DESK_LABEL="$DESKTOP_PATH"
fi
say "   Atalho desktop: $DESK_LABEL"
say "   Log:           $LOG_FILE"
say ""
say "Como abrir agora:"
if [ "$IN_PATH" = "1" ]; then
  say "   handheld-launcher"
else
  say "   export PATH=\"\$HOME/.local/bin:\$PATH\" && handheld-launcher"
  say "   (ou procura \"$APP_TITLE\" no menu de aplicações)"
fi
say ""
say "Se a janela não abrir, corre isto para ver o erro e reporta-o:"
say "   $INSTALL_DIR/$APP_NAME"
say "Diagnóstico rápido (bibliotecas e serviços de arranque, sem abrir janela):"
say "   $INSTALL_DIR/$APP_NAME --selftest"

if [ "$RUN_AFTER" = "1" ] && [ "$DRY_RUN" = "0" ]; then
  say ""
  say ">> A abrir o launcher..."
  if command -v setsid >/dev/null 2>&1; then
    setsid "$INSTALL_DIR/$APP_NAME" >/dev/null 2>&1 &
  else
    "$INSTALL_DIR/$APP_NAME" >/dev/null 2>&1 &
  fi
fi

exit 0




