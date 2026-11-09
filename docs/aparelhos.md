# Aparelhos — o que funciona em cada um

Atualizado na v0.24.2 (Linux nativo em preparação). "Validado" = testado no aparelho real.

| Recurso | Legion Go 2 (Windows) | ROG Ally / Ally X (Windows) | Linux (Bazzite) |
| --- | --- | --- | --- |
| Biblioteca, capas, detalhes | Validado | Funciona | Funciona (Steam/Epic/GOG/Heroic/Eden) |
| Quick Settings (L3+R3 / Ctrl+Alt+O) | Validado | Funciona | Funciona (evdev) |
| TDP | Validado (5–30 W, PawnIO) | Mesma faixa, **não validado** | `ryzenadj` (a validar no aparelho) |
| Curva de ventoinha | Validado (API da Lenovo) | Não disponível | Backlog |
| Modo de cor da tela | Validado | Funciona | Backlog (`wl-gammarelay`) |
| Brilho / Volume | Validado | Funciona | `brightnessctl` / `pactl` |
| Lossless Scaling | Validado | Funciona | LSFG-VK (frame-gen Vulkan; o QS gere 2x/3x/4x) |

## Problemas conhecidos por aparelho

**Legion Go 2**
- Às vezes o controle embutido não responde depois de ligar (aparece só no "slot 1" do Windows). Abrir o Legion Space resolve; a v0.16.4 tenta fazer isso sozinha.

**ROG Ally / Ally X**
- TDP sem validação própria; ajuste de TDP do Ally previsto na v0.19.

**Linux (Bazzite)**
- Sem validação em aparelho real ainda — o código nativo (TDP/brilho/volume/tela/rede/gamepad) está pronto, mas falta testar no handheld.
