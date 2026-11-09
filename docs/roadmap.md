# Roadmap

Resumo do plano estratégico. Cada versão só começa quando a anterior está pronta.

## Em curso (0.24.x → 1.0)

| Frente | Estado |
| --- | --- |
| Quick Settings 2.0 (brilho, TDP 8/15/20 W, resolução, D-pad) | Lançada na 0.24.2 |
| **Linux nativo (Bazzite)** | Em curso: multi-target `net8.0` + providers de hardware/rede/gamepad Linux prontos no código; falta testar no aparelho |
| Remover painel legado (QS antigo) | Planeado — só depois de validar o QS 2.0 no Linux |
| Perfis por jogo (TDP/resolução/ventoinha por jogo) | Planeado |
| Instalador MSI (Windows) | Backlog |

## Versões planeadas

| Versão | Foco |
| --- | --- |
| 0.25 | Assistente de instalação + fecho da tabela de versões |
| 1.0 | Interface em inglês, assinatura de código, documentação |

## Backlog anotado (nov/2026)

- **HidHide não tira o controle a um jogo JÁ ABERTO** — *device firewall*; a solução real é uma **camada de controle virtual (feeder)**, que no Linux é o caminho natural (evdev/uinput). Documentado; não bloqueia.
- **Ecrã inteiro exclusivo** = preto atrás do QS (limitação DirectX); no Linux/Gamescope (Wayland) o compositor não tem essa limitação.
- **Tabela antiga removida**: o roadmap estava 10 versões atrasado; consolidado aqui em nov/2026.

## Decisões

- O layout Clássico **fica**.
- O modo "Uso em PC" continua; o padrão é Handheld em tela cheia.
- **Distro alvo do Linux: Bazzite** (Gamescope/Wayland, ryzenadj, MangoHud). Distribuição via zip nativo + `install-linux.sh`; Flatpak/AppImage depois.
- Mascaramento do controle (HidHide) é Windows-only; no Linux o isolamento sai do feeder virtual (backlog).
