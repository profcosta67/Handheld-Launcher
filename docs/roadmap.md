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

- **Controle não responde no teclado virtual do Windows (TabTip).** Quando um campo de texto abre o teclado do sistema, o D-Pad/A/B não navegam nas teclas — o TabTip é uma janela do próprio Windows, fora da árvore do Avalonia, então o nosso leitor de XInput não a alcança. Impacta: senha do Wi‑Fi, busca da aba Nuvem e campos das Configurações. Caminhos a investigar, do mais barato ao mais caro:
  1. manter o teclado do launcher (o `TouchKeyboard` já existe) como padrão e só abrir o TabTip a pedido;
  2. esconder o controle do TabTip via HidHide e injetar teclas por `SendInput`/UIAutomation a partir do D-Pad;
  3. teclado próprio em Avalonia (na tela), reutilizando o layout do `TouchKeyboard` — resolve de vez e não depende do Windows.
- **Tabela de versões acima desatualizada**: o projeto está na **0.24.0** e a tabela ainda lista a 0.17 como "Próxima". Atualizar na 0.25, junto com o assistente de instalação.

## Decisões
- O layout Clássico **fica** (será lapidado na 0.17).
- O modo "Uso em PC" continua; o padrão é Handheld em tela cheia.
