# Levantamento do design (início da v0.17 "Lapidação")

Feito em 05/11/2026 sobre a branch `feature/v0.16.5`. Serve de ponto de partida
para o sistema de design: o que existe, o que está solto e a ordem sugerida
para arrumar. Nada aqui muda o visual por si só.

## O que existe

`src/HandheldLauncher.App/Styles/Theme.axaml` já guarda as peças centrais:

- 7 cores (fundo, superfície, superfície elevada, destaque, destaque suave,
  texto, texto secundário) mais barra, véu e texto sobre o destaque.
- 6 tamanhos de letra (base, corpo, título, sutil, legenda, cabeçalho de menu),
  reescalados por Pequeno/Médio/Grande em `ThemeManager.ApplyFontSize`.
- 4 raios (tile, barra, hero, pílula).
- 11 paletas em `ThemeManager.Palettes`, 11 fontes em `ThemeManager.Fonts`.

## O que está solto

| Item | Hoje |
| --- | --- |
| Cores escritas à mão em `.axaml` | 289 usos, 103 tons diferentes |
| Cinza `#252A36` | 42 usos, não muda com o tema |
| Arquivos com mais cores soltas | OverlayWindow (64), BigScreenLibraryLayout (40), SettingsWindow (38), MainWindow (35) |
| Cores soltas em `.cs` | ThemeManager (15), MainViewModel (8), OverlayWindow (7), GameTileViewModel (3), SubFilterViewModel (2) |
| Tamanhos de letra em `.axaml` | 19 valores (9 a 36), 239 usos |
| Raios de borda em `.axaml` | 13 valores mais 1 composto, 118 usos; 8 e 12 somam 76 |
| Fontes escritas à mão | Consolas (3), Inter (1), Georgia (1) |
| Cores de estado (sucesso, aviso, erro) | espalhadas: `#8FE3AC`, `#D6F5E0`, `#3FB950`, `#FFC45C`, `#FF4040`... |

Contraste do texto sobre a cor de destaque (aba ativa, botão principal), regra
anterior: Verde 2,1:1, Rosa 3,0:1, Azul 3,3:1. Corrigido nesta branch
(`ThemeManager.TextOnAccent`): branco só quando tem pelo menos 4:1, senão
quase-preto. Escuro, Vermelho e Grafite seguem iguais.

## Proposta de sistema

1. **Cores**: manter as 7 + acrescentar chaves nomeadas para o que hoje é
   valor solto: borda/divisor, hover, tile, estado (sucesso, aviso, erro) e
   véu. Cada paleta define todas, então tema novo = uma linha.
2. **Letras**: 8 degraus (11, 12, 14, 16, 18, 22, 30, 36 px), todos
   `DynamicResource` para continuar obedecendo Pequeno/Médio/Grande.
3. **Bordas**: 5 degraus (4, 8, 12, 16, 24 px) mais a pílula.
4. **Fontes**: família vem do tema/fonte escolhida; nenhuma tela escreve a sua
   (exceção: o texto monoespaçado de diagnóstico).

## Ordem de migração (uma tela por vez)

1. Infraestrutura: novas chaves em `Theme.axaml` e em cada paleta, com o mesmo
   valor de hoje (nenhuma mudança visível).
2. OverlayWindow, BigScreenLibraryLayout, SettingsWindow, MainWindow (os 4
   maiores), depois as janelas menores.
3. Cada tela: trocar valor solto pela chave, conferir no CI e no aparelho antes
   da próxima. Onde o valor solto vira chave com valor diferente (ex.: o cinza
   fixo passando a acompanhar o tema), registrar na nota da versão.

## Decisões que dependem do usuário

- Quais paletas ficam, quais saem e quais entram (página de comparação publicada
  como artifact "Paletas e fontes do Launcher").
- Quais fontes entram no pacote (cada TTF livre pesa algumas centenas de KB).
- Aprovar as escalas de letra e de borda.

## Ícones

Hoje são emojis do sistema, escritos à mão em cada tela: **172 usos, 50
símbolos diferentes**. Problemas:

- **Sem padrão**: alguns itens têm ícone, outros não, sem critério visível.
- **Sem relação com o que fazem**: o Lossless Scaling usa 🦆 (o apelido
  "Patinho" interno virou ícone sem querer); "Verificações" e "Central de
  Ajuda" usam 🩺 (estetoscópio) em dois lugares diferentes sem ligação clara.
- **Dependem da fonte de emoji do Windows**: cor, traço e peso mudam com a
  versão do Windows; não acompanham o tema (claro/escuro) nem a cor de
  destaque escolhida.
- Os mais usados: → (navegação/seta, 86×), 🟢 (ligado, 27×), 🎮 (controle,
  9×), ❌/✕ (fechar/cancelar, 23× somados), ⚡ (energia, 7×).

**Proposta**: trocar por um conjunto de ícones de linha (um só estilo, peso
único, ex. Lucide ou Phosphor — licença livre, SVG, cor controlada pelo tema)
só onde o ícone ajuda a entender ou a escanear rápido no controle (menu
global, abas de Configurações, estados ligado/desligado, ações destrutivas).
Texto sozinho nos itens onde o ícone de hoje só decora sem ajudar. Faz parte
da mesma varredura tela a tela da seção anterior.
