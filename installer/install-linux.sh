#!/usr/bin/env bash
# Instalador do Handheld Launcher no Linux (alvo principal: Bazzite).
# Uso: extraia HandheldLauncher-linux-x64.zip e rode  ./install-linux.sh
set -euo pipefail

APP_NAME="HandheldLauncher"
INSTALL_DIR="${HOME}/.local/share/handheld-launcher"
BIN_DIR="${HOME}/.local/bin"
AUTOSTART_DIR="${HOME}/.config/autostart"

echo "== Handheld Launcher — instalador Linux =="

# 1. Dependências (melhor esforço). No Bazzite (imutável) o ryzenadj, o
#    brightnessctl, o nmcli e o mangohud já vêm na imagem — aqui só avisamos.
echo ">> Verificando dependências (ryzenadj/brightnessctl/nmcli/ffmpeg)..."
missing=0
for tool in ryzenadj brightnessctl nmcli ffmpeg; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "   ! '$tool' não encontrado — a função associada pode não aplicar."
    missing=1
  fi
done
if [ "$missing" = "1" ]; then
  echo "   Instale com o gestor da sua distro (ex.: 'dnf install ryzenadj brightnessctl ffmpeg')."
fi

# 2. Binário
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$AUTOSTART_DIR"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "$HERE/$APP_NAME" ]; then
  cp "$HERE/$APP_NAME" "$INSTALL_DIR/"
  chmod +x "$INSTALL_DIR/$APP_NAME"
  echo ">> Binário instalado em $INSTALL_DIR"
else
  echo "!! Não encontrei '$APP_NAME' ao lado do script. Extraia todo o zip primeiro."
  exit 1
fi

# 3. Atalho no PATH
ln -sf "$INSTALL_DIR/$APP_NAME" "$BIN_DIR/handheld-launcher"
echo ">> Atalho 'handheld-launcher' criado em $BIN_DIR"

# 4. Auto-arranque (modo jogo: abre em tela cheia no login — o equivalente da
#    Tarefa Agendada do Windows). Para "iniciar igual ao Windows", ative o Modo
#    Kiosk nas Configurações do launcher (WindowState.FullScreen).
cat > "$AUTOSTART_DIR/handheld-launcher.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Handheld Launcher
Comment=Launcher para handhelds (modo jogo)
Exec=$INSTALL_DIR/$APP_NAME
Terminal=false
X-GNOME-Autostart-enabled=true
EOF
echo ">> Auto-arranque configurado em $AUTOSTART_DIR"

echo "== Pronto. Corra 'handheld-launcher' ou reinicie a sessão. =="
