## v0.10.21 - Capas Animadas (Animated Covers) 🎞️

O Handheld Launcher agora suporta **capas animadas** (GIFs e WebPs) do SteamGridDB!

### ✨ Novidades
- **Suporte a Animações:** Agora o launcher consegue exibir as capas animadas (animated) nativamente na biblioteca principal e na tela da roleta (tema Switch).
- **Filtro no Seletor:** Na tela de escolher capa, adicionamos o botão **"Só animadas"**. Ative ele para ver apenas as capas em movimento (perfeito para quem quer dar vida ao launcher).
- **Mais opções de capa:** Aumentamos o limite de busca de 24 para 50 opções, garantindo que as capas animadas (que costumam ter menos votos) não sejam escondidas.
- **Visual do Seletor:** Ajuste no preenchimento das miniaturas para que as imagens preencham a caixa inteira, sem bordas brancas.

### 🛠 Melhorias Técnicas
- Integração da biblioteca `AnimatedImage.Avalonia` (via `ImageBehavior`).
- Refatoração da API do SteamGridDB (`ISteamGridDbClient`) e do `ArtworkService` para suportar o filtro de animação.

*(Nota: O suporte a animações foi feito de forma cross-platform, então deve funcionar tanto no Windows quanto no Linux/Steam Deck!)*