#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Empacota o Handheld Launcher para Linux (linux-x64) e TESTA o pacote.
#
#   bash scripts/package-linux.sh --publish publish-linux --version 0.24.3-beta.5
#
# Gera, na pasta --out (padrão: raiz do repo):
#   HandheldLauncher-linux-x64.tar.gz   ← recomendado (preserva o bit +x)
#   HandheldLauncher-linux-x64.zip      ← alternativa (permissões fixadas antes)
#
# Porque é que isto corre num runner Linux (v0.24.3-beta.5): o Compress-Archive
# do windows-latest perdia as permissões e o zip chegava ao Zorin com
# "HandheldLauncher" e "install-linux.sh" SEM +x — o instalador copiava os
# ficheiros, mas nada era executável, e o utilizador ficava com uma instalação
# inútil. Aqui: chmod antes de compactar, e no fim o próprio arquivo gerado é
# extraído e instalado num HOME temporário para provar que funciona.
# ---------------------------------------------------------------------------

if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi

set -euo pipefail

NAME="handheld-launcher"          # pasta na raiz do arquivo compactado
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="publish-linux"
VERSION=""
OUT="$ROOT"
MAKE_ZIP=1
SKIP_VERIFY=0

usage() {
  cat <<'EOF'
Uso: bash scripts/package-linux.sh [opções]

  --publish DIR     Pasta com a publicação net8.0/linux-x64 (padrão: publish-linux)
  --version VER     Versão a gravar em version.txt (padrão: conteúdo de VERSION)
  --out DIR         Onde escrever os arquivos (padrão: raiz do repo)
  --no-zip          Só gera o .tar.gz (para máquinas sem o comando zip)
  --skip-verify     Não extrai/instala o resultado (não recomendado)
  -h, --help        Esta ajuda
EOF
}

fail() { printf '!! %s\n' "$*" >&2; exit 1; }

while [ "$#" -gt 0 ]; do
  case "$1" in
    --publish)      [ "$#" -ge 2 ] || fail "--publish precisa de uma pasta."; PUBLISH="$2"; shift ;;
    --version)      [ "$#" -ge 2 ] || fail "--version precisa de um valor.";  VERSION="$2"; shift ;;
    --out)          [ "$#" -ge 2 ] || fail "--out precisa de uma pasta.";     OUT="$2";    shift ;;
    --no-zip)       MAKE_ZIP=0 ;;
    --skip-verify)  SKIP_VERIFY=1 ;;
    -h|--help)      usage; exit 0 ;;
    *)              fail "Opção desconhecida: $1 (usa --help)." ;;
  esac
  shift
done

[ -d "$PUBLISH" ] || fail "Pasta de publicação '$PUBLISH' não existe."
[ -f "$PUBLISH/HandheldLauncher" ] || fail "'$PUBLISH/HandheldLauncher' não existe (publicaste com -r linux-x64?)."

if [ -z "$VERSION" ]; then
  [ -f "$ROOT/VERSION" ] || fail "VERSION não encontrado; passa --version."
  VERSION="$(tr -d ' \r\n' < "$ROOT/VERSION")"
fi
VERSION="$(printf '%s' "$VERSION" | tr -d ' \r\n')"

mkdir -p "$OUT"
# --out pode vir relativo (o CI passa "--out ."). Normaliza já: mais abaixo há um
# `cd` num subshell para criar o zip, e com um caminho relativo o arquivo acabava
# gravado dentro da pasta temporária (desaparecia no trap do EXIT) em vez da
# pasta pedida — foi o que reprovou o job linux-package do beta.5.
OUT="$(cd "$OUT" && pwd)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

DEST="$STAGE/$NAME"
# Layout do pacote (0.24.3-beta.7): a raiz fica com o ÚNICO atalho visível
# (INSTALAR.desktop) para ninguém andar à procura do instalador; os binários
# vão para arquivos/ e os scripts para instalador/.
SRC_APP="$DEST/arquivos"
SRC_INST="$DEST/instalador"
mkdir -p "$SRC_APP" "$SRC_INST"
cp -a "$PUBLISH/." "$SRC_APP/"

# Ferramentas Windows (ffmpeg.exe/HidHide/PawnIO) não têm lugar num pacote Linux.
if [ -d "$SRC_APP/Tools" ] && ls "$SRC_APP/Tools"/*.exe >/dev/null 2>&1; then
  fail "Tools/ tem binários Windows no pacote Linux — ver a condição de TFM no HandheldLauncher.App.csproj."
fi

# Instalador, desinstalador, ícone e versão viajam dentro do pacote: é o que
# permite instalar/desinstalar sem voltar a descarregar nada.
cp "$ROOT/installer/install-linux.sh"   "$SRC_INST/install-linux.sh"
cp "$ROOT/installer/uninstall-linux.sh" "$SRC_INST/uninstall-linux.sh"
cp "$ROOT/installer/instalar.sh"        "$SRC_INST/instalar.sh"
cp "$ROOT/installer/LEIA-ME.txt"        "$SRC_INST/LEIA-ME.txt"
cp "$ROOT/installer/INSTALAR.desktop"   "$DEST/INSTALAR.desktop"
[ -f "$ROOT/installer/icons/handheld-launcher.png" ] || fail "Falta installer/icons/handheld-launcher.png."
cp "$ROOT/installer/icons/handheld-launcher.png" "$SRC_APP/handheld-launcher.png"
printf '%s\n' "$VERSION" > "$SRC_APP/version.txt"

# Permissões ANTES de compactar — é isto que o Compress-Archive do Windows não
# fazia e que deixava a instalação inútil no Zorin.
chmod 755 "$SRC_APP/HandheldLauncher" \
          "$SRC_INST/install-linux.sh" "$SRC_INST/uninstall-linux.sh" "$SRC_INST/instalar.sh"
# O .desktop da raiz tem de ser executável para o duplo-clique (Nautilus/KDE).
chmod 755 "$DEST/INSTALAR.desktop"

TARBALL="$OUT/HandheldLauncher-linux-x64.tar.gz"
ZIPFILE="$OUT/HandheldLauncher-linux-x64.zip"
rm -f "$TARBALL"
tar -C "$STAGE" -czf "$TARBALL" "$NAME"
echo ">> $TARBALL"

if [ "$MAKE_ZIP" = "1" ]; then
  command -v zip >/dev/null 2>&1 || fail "'zip' não encontrado (instala-o ou usa --no-zip)."
  rm -f "$ZIPFILE"
  ( cd "$STAGE" && zip -rq "$ZIPFILE" "$NAME" )
  echo ">> $ZIPFILE"
fi

if [ "$SKIP_VERIFY" = "1" ]; then
  echo ">> verificação ignorada (--skip-verify)"
  exit 0
fi

# ---------------------------------------------------------------------------
# Verificação: extrai o arquivo tal como o utilizador vai fazer, corre o
# instalador num HOME temporário e confirma que o resultado é utilizável.
# ---------------------------------------------------------------------------
verify_pack() {  # $1 = pasta extraída (contém $NAME/), $2 = rótulo
  local pack="$1" label="$2"
  local app="$pack/$NAME"
  local bin="$app/arquivos/HandheldLauncher"
  local inst="$app/instalador"
  local work ihome desk autostart desk_top topo
  work="$(mktemp -d)"
  ihome="$work/home"
  # O utilizador tem de ter um ambiente de trabalho para receber o atalho.
  mkdir -p "$ihome/Desktop"

  # A raiz do pacote tem de ter SÓ estes três nomes: é o que o utilizador vê ao
  # extrair, e serve para ele não andar à procura do instalador.
  topo="$(find "$app" -mindepth 1 -maxdepth 1 -printf '%f\n' | sort | tr '\n' ' ')"
  [ "$topo" = "INSTALAR.desktop arquivos instalador " ] \
    || { echo "!! [$label] raiz do pacote inesperada: '$topo'" >&2; return 1; }
  [ -x "$app/INSTALAR.desktop" ] || { echo "!! [$label] INSTALAR.desktop sem +x" >&2; return 1; }
  [ -x "$bin" ]                 || { echo "!! [$label] arquivos/HandheldLauncher sem +x" >&2; return 1; }
  [ -x "$inst/instalar.sh" ]        || { echo "!! [$label] instalador/instalar.sh sem +x" >&2; return 1; }
  [ -x "$inst/install-linux.sh" ]   || { echo "!! [$label] instalador/install-linux.sh sem +x" >&2; return 1; }
  [ -x "$inst/uninstall-linux.sh" ] || { echo "!! [$label] instalador/uninstall-linux.sh sem +x" >&2; return 1; }
  [ -f "$app/arquivos/handheld-launcher.png" ] || { echo "!! [$label] ícone em falta" >&2; return 1; }
  [ -f "$app/arquivos/version.txt" ]           || { echo "!! [$label] version.txt em falta" >&2; return 1; }
  if ! file -b "$bin" | grep -q 'ELF 64-bit'; then
    echo "!! [$label] HandheldLauncher não é ELF 64-bit" >&2
    return 1
  fi
  if command -v desktop-file-validate >/dev/null 2>&1; then
    local dv_inst
    dv_inst="$(desktop-file-validate "$app/INSTALAR.desktop" 2>&1 || true)"
    if printf '%s' "$dv_inst" | grep -q 'error:'; then
      printf '%s\n' "$dv_inst" >&2
      echo "!! [$label] INSTALAR.desktop inválido" >&2
      return 1
    fi
    [ -z "$dv_inst" ] || printf '   (aviso) %s\n' "$dv_inst"
  fi

  # Caminho do utilizador: o menu do instalador, em modo não-interativo
  # (opção 1 = instalar + arranque automático no login).
  if ! ( cd "$app" && HOME="$ihome" bash ./instalador/instalar.sh --sem-perguntas --no-add-path ) > "$work/install.log" 2>&1; then
    echo "!! [$label] instalador/instalar.sh falhou:" >&2
    cat "$work/install.log" >&2
    return 1
  fi

  desk="$ihome/.local/share/applications/handheld-launcher.desktop"
  [ -x "$ihome/.local/share/handheld-launcher/HandheldLauncher" ] \
    || { echo "!! [$label] binário não ficou executável em ~/.local/share" >&2; return 1; }
  [ -L "$ihome/.local/bin/handheld-launcher" ] \
    || { echo "!! [$label] atalho ~/.local/bin/handheld-launcher em falta" >&2; return 1; }
  [ "$(readlink -f "$ihome/.local/bin/handheld-launcher")" = "$(readlink -f "$ihome/.local/share/handheld-launcher/HandheldLauncher")" ] \
    || { echo "!! [$label] atalho aponta para o sítio errado" >&2; return 1; }
  [ -f "$desk" ] || { echo "!! [$label] entrada de menu em falta" >&2; return 1; }
  [ -f "$ihome/.local/share/icons/hicolor/256x256/apps/handheld-launcher.png" ] \
    || { echo "!! [$label] ícone não foi instalado" >&2; return 1; }
  grep -q '^Icon=handheld-launcher$' "$desk" \
    || { echo "!! [$label] entrada de menu sem Icon=" >&2; return 1; }
  grep -qxF "Exec=\"$ihome/.local/share/handheld-launcher/HandheldLauncher\"" "$desk" \
    || { echo "!! [$label] entrada de menu com Exec= errado" >&2; return 1; }
  if command -v desktop-file-validate >/dev/null 2>&1; then
    local dv
    dv="$(desktop-file-validate "$desk" 2>&1 || true)"
    if printf '%s' "$dv" | grep -q 'error:'; then
      printf '%s\n' "$dv" >&2
      echo "!! [$label] .desktop inválido" >&2
      return 1
    fi
    [ -z "$dv" ] || printf '   (aviso) %s\n' "$dv"
  fi

  # Arranque automático no login (o instalador liga-o por omissão) e atalho no
  # ambiente de trabalho — eram exactamente as duas coisas que faltavam.
  autostart="$ihome/.config/autostart/handheld-launcher.desktop"
  desk_top="$ihome/Desktop/handheld-launcher.desktop"
  [ -f "$autostart" ] || { echo "!! [$label] arranque automático não foi criado" >&2; return 1; }
  grep -q '^X-GNOME-Autostart-enabled=true$' "$autostart" \
    || { echo "!! [$label] autostart sem X-GNOME-Autostart-enabled=true" >&2; return 1; }
  [ -x "$desk_top" ] || { echo "!! [$label] atalho no ambiente de trabalho em falta" >&2; return 1; }
  grep -qxF "Exec=\"$ihome/.local/share/handheld-launcher/HandheldLauncher\"" "$desk_top" \
    || { echo "!! [$label] atalho do desktop com Exec= errado" >&2; return 1; }

  # Autoteste dentro do processo (`HandheldLauncher --selftest`): carrega todas
  # as assemblies de que o launcher depende e constrói os serviços de arranque,
  # sem abrir janela. Foi esta a falha do beta.5 — o pacote extraía, instalava e
  # passava todas as verificações acima, e depois o binário morria no arranque
  # (FileNotFoundException de LibreHardwareMonitorLib, que o publish linux-x64
  # não copiava). Sem este passo, o defeito só aparecia ao utilizador final.
  if ! HOME="$ihome" timeout 180 "$ihome/.local/share/handheld-launcher/HandheldLauncher" --selftest \
        > "$work/selftest.log" 2>&1; then
    echo "!! [$label] --selftest falhou — o launcher não arranca com este pacote:" >&2
    cat "$work/selftest.log" >&2
    return 1
  fi
  if ! grep -q 'AUTOTESTE OK' "$work/selftest.log"; then
    echo "!! [$label] --selftest não confirmou sucesso:" >&2
    cat "$work/selftest.log" >&2
    return 1
  fi
  # Só aparece na consola do CI se se imprimir; sem isto o passo passava calado
  # e não se sabia se o autoteste chegou a correr.
  echo ">> [$label] autoteste de arranque: OK ($(grep -c '   ok   ' "$work/selftest.log") verificações, $(grep -c 'ok   assembly' "$work/selftest.log") assemblies)"

  # Opção 2 do menu: instalar sem arranque automático tem de remover o anterior
  # (é o que impede o "ligou sozinho e eu não pedi" em PC/desktop).
  if ! ( cd "$app" && HOME="$ihome" bash ./instalador/instalar.sh --sem-perguntas --no-autostart --no-add-path ) \
        > "$work/install2.log" 2>&1; then
    echo "!! [$label] reinstalação sem autostart falhou:" >&2
    cat "$work/install2.log" >&2
    return 1
  fi
  [ ! -e "$autostart" ] \
    || { echo "!! [$label] --no-autostart não removeu o arranque automático" >&2; return 1; }

  # Opção 3 do menu: o desinstalador tem de limpar tudo.
  if ! ( cd "$app" && HOME="$ihome" bash ./instalador/instalar.sh --sem-perguntas --desinstalar ) \
        > "$work/uninstall.log" 2>&1; then
    echo "!! [$label] desinstalação falhou:" >&2
    cat "$work/uninstall.log" >&2
    return 1
  fi
  [ ! -e "$ihome/.local/share/handheld-launcher" ] \
    || { echo "!! [$label] desinstalação deixou a aplicação" >&2; return 1; }
  [ ! -e "$ihome/.local/bin/handheld-launcher" ] \
    || { echo "!! [$label] desinstalação deixou o atalho" >&2; return 1; }
  [ ! -e "$desk" ] || { echo "!! [$label] desinstalação deixou a entrada de menu" >&2; return 1; }
  [ ! -e "$desk_top" ] || { echo "!! [$label] desinstalação deixou o atalho do desktop" >&2; return 1; }

  echo ">> [$label] instala → autostart → desktop → menu → ldd → autoteste → desinstala: OK"
  rm -rf "$work"
  return 0
}

echo ">> Verificando o .tar.gz (extrair + instalar num HOME temporário)"
VWORK="$(mktemp -d)"
tar -C "$VWORK" -xzf "$TARBALL"
verify_pack "$VWORK" "tar.gz" || { rm -rf "$VWORK"; fail "pacote .tar.gz reprovado."; }
rm -rf "$VWORK"

if [ "$MAKE_ZIP" = "1" ]; then
  command -v unzip >/dev/null 2>&1 || fail "'unzip' não encontrado (necessário para verificar o zip)."
  echo ">> Verificando o .zip"
  VWORK="$(mktemp -d)"
  ( cd "$VWORK" && unzip -q "$ZIPFILE" )
  verify_pack "$VWORK" "zip" || { rm -rf "$VWORK"; fail "pacote .zip reprovado."; }
  rm -rf "$VWORK"
fi

echo ">> Pacotes prontos:"
ls -lh "$TARBALL"
if [ "$MAKE_ZIP" = "1" ]; then
  ls -lh "$ZIPFILE"
fi

