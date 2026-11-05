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
da mesma varredura tela a tela da seção anterior — a troca pra um conjunto de
linha único ainda não começou (pede escolher a biblioteca e trocar emoji por
SVG em cada tela, uma de cada vez).

## v0.17 — implementado nesta leva

Aprovado pelo usuário e feito nesta branch, de uma vez (não tela a tela, por
serem mudanças aditivas/isoladas, não uma reescrita de telas existentes):

- **5 paletas novas** em `ThemeManager.Palettes`/`ThemeNames`: Meia-noite,
  Crepúsculo, Musgo, Névoa e Alto Contraste — hex exatos do comparativo
  publicado. `TextOnAccent` já decide automaticamente a cor do texto sobre o
  destaque de cada uma (checado nas 16 paletas: nenhuma ficou abaixo de 4:1).
- **9 fontes novas** embutidas em `Assets/Fonts/` (Manrope, Outfit, Space
  Grotesk, DM Sans, Plus Jakarta Sans, Chakra Petch, Rajdhani, Nunito,
  Fredoka) — baixadas do google/fonts, instanciadas Regular/Bold com
  `fontTools` (a versão variável original resolvia peso errado, ex. "Manrope
  ExtraLight"), licença OFL documentada em `Assets/Fonts/FONTES-LICENCA.txt`.
- **Sugestões de layout atualizadas**: Clássico → Plus Jakarta Sans, Clean →
  Manrope, BigScreen → Chakra Petch, Cartoon (reservado) → Fredoka (antes
  Segoe UI/Inter/Bahnschrith/Comic Sans). Só afeta quem troca de layout a
  partir de agora; quem já escolheu fonte em Preferências não é alterado.
- **Infraestrutura das escalas**: os 8 degraus de letra e os 5 de borda da
  seção "Proposta de sistema" acima agora existem como recursos nomeados
  (`TypeScale1..8`, `RadiusScale1..5`) em `Theme.axaml`, e os de letra já
  reescalam com Pequeno/Médio/Grande (`ThemeManager.ApplyFontSize`). Isto é só
  a base: nenhuma tela foi migrada pra usar essas chaves ainda — os ~350
  valores soltos (cor, fonte, tamanho, raio) continuam onde estavam, e trocar
  cada um é o trabalho tela-a-tela da seção "Ordem de migração", ainda não
  iniciado.
- **Ícones "superficiais" corrigidos**: 🦆 (Lossless Scaling, resquício do
  apelido interno "Patinho") trocado por 🖼 em `SettingsWindow.axaml`,
  `OverlayWindow.axaml` e no comentário de `SettingsViewModel.cs`; 🩺 (usado
  em "Central de Ajuda" sem relação clara com estetoscópio) trocado por 🛟 em
  `MainViewModel.cs` e `HelpCenterWindow.axaml` — mantido só em "Verificações"
  (diagnóstico, onde o estetoscópio faz sentido).
- **Levantamento estático de botões clicáveis**: busca em todos os `.axaml`
  por `Button`/`ToggleButton` sem `Click`, `Command` ou `IsChecked` — e,
  quando wireado via `x:Name` no code-behind em vez do atributo, confirmado
  manualmente lá. Nenhum botão sem ligação foi encontrado nesta passada
  (Conectividade, filtros de biblioteca, download de artwork, atalhos,
  toggle do Lossless Scaling — todos checados). **Limite importante**: isto é
  só uma varredura de CÓDIGO (o quê está ligado a quê) — não é uma inspeção
  visual. Alinhamento, botão cortado, texto vazando ou sobreposição entre
  temas só aparecem olhando o aparelho de verdade; sem acesso a tela do
  Windows/Avalonia daqui, essa parte da checagem depende de screenshots do
  usuário (ideal: uma foto por tema/layout depois do próximo beta).
