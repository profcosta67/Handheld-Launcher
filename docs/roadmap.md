# Roadmap

Resumo do plano estratégico. Cada versão só começa quando a anterior está pronta.

| Versão | Nome | Foco | Estado |
| --- | --- | --- | --- |
| 0.16 | Alicerce | Faxina, processo de versão, canal beta, Central de Ajuda | 0.16.3 lançada; 0.16.4 fecha os arremates |
| 0.17 | Lapidação | Sistema de design (cores, fontes, ícones num lugar só), personalização ligada ao tema, layout Clássico lapidado, fechamento da Central de Ajuda | Próxima |
| 0.18 | Detalhes 2.0 | Página de jogo em tela cheia, modo xCloud, card de jogos grátis | Planejada |
| 0.19 | Multi-aparelho | Perfis por aparelho, TDP do ROG Ally, HidHide, Saúde do aparelho, assistente de primeira abertura | Planejada |
| 0.20 | Receitas | Receitas por jogo, diário de sessão, previsão de bateria | Planejada |
| 1.0 | Lançamento | Interface em inglês, assinatura de código, documentação | Planejada |

## Futuro (depois da 1.0)
Guardião de suspensão, gerenciador de armazenamento, alertas de saúde do aparelho, orçamento de bateria completo, Central de saves, agenda do Game Pass, tema Cartoon, replay instantâneo.

## Backlog anotado (nov/2026)

- **HidHide não tira o controle a um jogo JÁ ABERTO** (descoberto em 09/11). O HidHide é um *device firewall*: decide quem vê o dispositivo no momento em que a aplicação **abre** o dispositivo. Um jogo que já tem o handle aberto continua a receber input, por isso mascarar no instante em que o Quick Settings abre não tem efeito sobre ele (o log mostra `Controle OCULTADO` e o jogo continua a reagir). Opções para fechar isto:
  1. **suspender o processo do jogo** enquanto o QS está aberto (`Platform/InputBlockService` já existe, foi o mecanismo antes da 0.23.4) — fiável, mas congela o vídeo;
  2. **camada de controle virtual** (feeder: esconder o físico do jogo e reexpor um virtual que o launcher controla, que é o caso de uso descrito no DEVELOPER.md do HidHide) — resolve a sério, mas é um projeto;
  3. aceitar a limitação e documentá-la (o mascaramento serve para isolar o controle de apps que arrancam depois).
- ~~**Controle não responde no teclado virtual do Windows (TabTip).**~~ **Resolvido na 0.24.1**: o launcher passou a ter teclado próprio em Avalonia (`Views/OnScreenKeyboardView` + `Core/Services/Keyboard`), com D-Pad/A/B, usado na senha do Wi‑Fi, nos campos das Configurações, Contas, Adicionar/Editar jogo e na busca da aba Nuvem.
- **Tabela de versões acima desatualizada**: o projeto está na **0.24.x** e a tabela ainda lista a 0.17 como "Próxima". Atualizar na 0.25, junto com o assistente de instalação.

## Decisões
- O layout Clássico **fica** (será lapidado na 0.17).
- O modo "Uso em PC" continua; o padrão é Handheld em tela cheia.
- **Mascaramento do controle (HidHide) não precisa de administrador** — medido na 0.24.1 (driver 1.5.230: `elevada=False` → `ativo=True`). Quem precisa de elevação é o **TDP** e o ligar/desligar de dispositivos **Bluetooth**.
