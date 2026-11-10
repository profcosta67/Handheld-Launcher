#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Handheld Launcher — atalho de instalação (duplo-clique).
#
# É o ÚNICO ficheiro na raiz do pacote. Abre o menu do instalador, que vive em
# instalador/menu.sh.
#
#   duplo-clique   → o gestor de ficheiros pergunta "Executar no terminal";
#                    escolhe essa opção (no GNOME/Zorin pode ser preciso
#                    botão direito → Permitir execução, só na 1.ª vez)
#   ./INSTALAR.sh  → o mesmo, a partir do terminal
#
# Porque um .sh e não um .desktop: um .desktop extraído de um tar não pode ter
# caminho absoluto no Exec, e resolvê-lo com %k obriga a `$`/aspas dentro do
# Exec — que o desktop-file-validate rejeita. Um .sh com +x abre com dois
# cliques no Nautilus, Dolphin, Thunar e no terminal, sem truques.
# ---------------------------------------------------------------------------

if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
MENU="$HERE/instalador/menu.sh"

if [ ! -f "$MENU" ]; then
  echo "!! Não encontrei 'instalador/menu.sh' ao lado deste atalho." >&2
  echo "   Extrai o pacote completo (handheld-launcher/ com instalador/ e arquivos/)." >&2
  if [ -t 0 ]; then
    printf '\nPrima Enter para fechar esta janela...'
    read -r _ || true
  fi
  exit 1
fi

exec bash "$MENU" "$@"
