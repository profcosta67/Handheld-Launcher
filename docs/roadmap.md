# Roadmap do Handheld Launcher — da 0.25 à 1.0

> Plano de produto atualizado em 16/11/2026 (reordenado: Windows primeiro, Linux
> depois da 1.0; sobras do plano original de 04/11 incorporadas). As versões abaixo são marcos
> propostos, sem datas prometidas. O conteúdo e a numeração podem ser ajustados
> após validação técnica e em aparelho real. Uma versão só é considerada pronta
> quando seus critérios de aceite forem cumpridos; código compilando, por si só,
> não é prova de que uma função de hardware funciona.

## Visão

Fazer do Handheld Launcher a central pessoal de jogos do portátil: biblioteca,
identidade visual própria, controles úteis, configurações por jogo e suporte
confiável ao aparelho. O launcher deve trabalhar com Steam, Bazzite e ferramentas
do fabricante quando elas já fazem bem o seu trabalho — não tentar substituí-las
nem disputar espaço copiando suas interfaces.

## Estado de partida

O repositório está na versão estável **0.26.2** (14/11/2026), com a **0.27 (TDP inteligente)** em beta: blocos 0.25 (estabilidade) e 0.26 (identidade visual) fechados. Já existem biblioteca multi-loja, layouts e temas,
detalhes e conquistas, Quick Settings, diário de sessões, Central de Ajuda,
armazenamento, saúde do aparelho, guardião de suspensão, rede, Syncthing e
atualizações beta. A versão 0.25 também acrescentou melhorias de instalação.
A 0.24 chegou a abrir sem administrador, mas a **0.25.7 voltou a exigir
administrador sempre** (`requireAdministrator` + tarefa de logon elevada) para
estabilizar TDP e ventoinha. Isso tem custos (jogos e lojas herdam o admin,
UAC fora da tarefa de logon) — ver [admin-helper.md](./admin-helper.md).

O refinamento visual 0.26 começou pelo layout Clássico, com proposta aprovada
pelo usuário. A linguagem foi expandida aos layouts BigScreen, Switch e Cartoon,
à nuvem compartilhada, aos detalhes do jogo e às configurações, preservando a
identidade de cada layout. As superfícies da Central de Controle e do Quick
Settings também receberam cantos e contornos mais consistentes. A conferência
visual, de navegação e do Acrílico em execução continua pendente no dispositivo.

O roadmap original foi escrito quando o projeto estava na 0.15.5. Vários dos
seus marcos já viraram produto; as maiores lacunas restantes são:

- **Receitas/perfis por jogo**: o diário de sessões existe, mas ainda não há
  aplicação automática e confiável de uma configuração por jogo.
- **Linux em aparelho real**: já há build nativo e vários providers Linux,
  porém falta validar a experiência completa no handheld. O estado atual do
  suporte está em [aparelhos.md](./aparelhos.md).
- **MangoHud no Linux**: o botão/estado ainda não controla o HUD; a implementação
  atual apenas registra a intenção no log.
- **Visual**: já há temas, layouts, recursos de tipografia e componentes, mas a
  auditoria de design ainda encontrou estilos e valores visuais espalhados. É
  preciso dar mais personalidade e profundidade à interface sem apagar a
  identidade existente.
- **Preparação para a 1.0**: localização em inglês, assinatura, instalação,
  documentação e validação por terceiros ainda precisam ser fechadas.

## Princípios de produto e execução

1. **Handheld primeiro, sem excluir o PC.** Navegação por controle, boa leitura
   em telas pequenas e diálogos funcionais são critérios de primeira classe.
2. **Identidade própria, não uma cópia.** Não replicar a interface SteamOS,
   Xbox, Armoury Crate ou Legion Space. Usar referências como inspiração de
   usabilidade, não como molde visual.
3. **Integração antes de duplicação.** Detectar e cooperar com Steam, Gamescope,
   MangoHud e serviços de hardware presentes no sistema. Não disputar o controle
   do TDP com outro gerenciador.
4. **Hardware com segurança.** Não instalar drivers, alterar limites ou ativar
   controle virtual silenciosamente. Explicar permissões, limites e riscos;
   testar por modelo antes de declarar suporte.
5. **Entrega por etapas pequenas e verificáveis.** Sem datas artificiais;
   regressão de biblioteca, controle ou acessibilidade bloqueia a conclusão do
   marco.
6. **Privacidade primeiro.** Backup e diagnósticos ficam locais. Qualquer
   compartilhamento de compatibilidade é voluntário, explícito e minimizado.

## Próximos marcos

### 0.25.x — Estabilidade antes do visual

**Objetivo:** fechar o que atrapalha o uso diário antes de seguir com o visual.
**Situação (12/11/2026):** fechado na 0.25.10, com as decisões abaixo.

1. Quick Settings dentro do jogo — **decidido.** Sem controle virtual, o Windows
   não permite ao mesmo tempo "jogo sem alteração atrás do painel" e "jogo sem
   receber o controle". Configurações → Overlay oferece três modos: pegar o foco
   (padrão, recomendado), não pegar o foco, congelar (opt-in com aviso de risco).
   Toques duplicados (ponte HID + XInput; controle virando teclado) corrigidos.
   Validado no Legion Go 2; **Ally pendente** (sem aparelho para teste).
2. Camada esbranquiçada — causa: perfil de cor Vívido. O launcher não acumula a
   curva; fica como problema conhecido ("use Padrão").
3. Administrador — **decidido:** continua sempre admin na 0.25. O ajudante
   privilegiado ([admin-helper.md](./admin-helper.md)) entra na 0.30, no
   Windows; a versão Linux fica para depois da 1.0.
4. Processo: branch de trabalho mesclado no master a cada oficial; VERSION
   sempre acima do último lançado.

**Para estudo depois da 0.26:** controle virtual (ViGEm) para isolar o jogo sem
tirar o foco — exige driver e tem risco com anti-cheat; só como opção explícita.

### 0.26 — Identidade visual e experiência coesas

**Objetivo:** deixar de parecer uma coleção de telas retangulares e aproximar a
interface de uma biblioteca de jogos portátil, mantendo a identidade visual do
Handheld Launcher e os layouts que as pessoas já usam.

**Passo a passo**

1. Registrar telas atuais nos tamanhos 1280×720, 1280×800 e 1920×1080, incluindo
   os layouts e temas existentes; identificar os problemas de hierarquia,
   densidade, foco e navegação.
2. Definir uma linguagem visual própria: capas e artes como protagonistas,
   composição assimétrica com áreas de respiro, superfícies em camadas, contraste
   claro entre conteúdo e controles e uma família consistente de formas. Cantos,
   contornos e cartões deixam de ser uniformemente quadrados, sem virar uma
   coleção de pílulas genéricas.
3. Atualizar primeiro a biblioteca e o detalhe do jogo como protótipo. Preservar
   paletas, tipografia e elementos reconhecíveis do launcher; manter o layout
   Clássico e o Cartoon. Não remover layout nem impor uma aparência nova a quem
   já escolheu outra.
4. Consolidar tokens de tema, tipografia, estados, bordas, elevação, ícones e
   foco do controle. Migrar os componentes mais visíveis e repetidos antes das
   telas secundárias.
5. Reduzir ornamentação e efeitos caros quando prejudicarem leitura, desempenho
   ou bateria. Animações devem ser breves, discretas e respeitar a preferência
   por movimento reduzido.
6. Revisar cada mudança no controle e nos tamanhos documentados; fazer uma
   rodada de beta antes de aplicar o padrão às demais telas.

**Critérios de aceite**

- As telas principais têm hierarquia visual clara e aparência consistente, sem
  parecer planilha ou cópia de uma marca concorrente.
- Biblioteca, filtros, detalhes, Quick Settings e configurações continuam
  navegáveis por controle e teclado.
- Nenhum texto ou controle fica cortado nos tamanhos testados; foco e contraste
  são perceptíveis nos temas suportados.
- As paletas e layouts existentes permanecem disponíveis durante a transição.

### 0.27 — TDP inteligente (em beta)

**Objetivo:** o launcher ajusta TDP e ventoinha sozinho para manter uma meta de
FPS, aprende o valor por jogo e sai da frente quando o usuário mexe à mão.

**Situação (16/11/2026):** em beta (beta.64). Entregue: controlador por meta de
FPS, botão Auto e meta no Quick Settings, meta por jogo, valor aprendido por
jogo e por meta, ventoinha ligada ao TDP, restauração ao sair. A base foi
publicada como estável **0.26.2**.

**Falta para fechar**

1. Confirmar a beta.64 no Legion Go 2 (EA FC com replays; um jogo leve; um jogo
   que não alcança a meta).
2. Aviso de meta inalcançável (hoje ele fica no teto calado).
3. Limites de tomada/bateria relidos no meio do jogo.
4. Restaurar o TDP do usuário se o launcher reiniciar com jogo aberto.

**Conhecido e aceito por ora:** quedas curtas muito frequentes ainda empurram o
TDP para cima; jogo com vsync que precisa de bem mais potência numa área pesada
demora a ser atendido. Só no Windows.

### 0.28 — Otimização e base (Windows)

**Objetivo:** launcher mais leve e código mais fácil de manter antes de crescer
de novo. Nenhum recurso novo visível além do que a otimização trouxer.

**Passo a passo**

1. Medir antes de mexer: tempo até a biblioteca ficar utilizável, memória e uso
   de CPU/GPU parado e com jogo aberto. Meta do plano original: pronto em até
   3 s no Legion Go 2.
2. Com jogo aberto, o launcher não deve gastar: rever timers, leituras de
   sensores e o que fica rodando em segundo plano.
3. Desfoque de fundo pré-calculado e guardado em cache por jogo (item da v0.17
   original que não foi feito); animações reduzidas na bateria.
4. Log mais enxuto: a sequência do PawnIO repete 15 linhas a cada troca de TDP.
5. Quebrar os arquivos gigantes: `OverlayWindow.axaml.cs` (2.231 linhas),
   `MainViewModel.cs` (2.167), `MainWindow.GameplayWatcher.cs` (1.427).
6. Reduzir o que esconde erro ou trava: 37 `catch` vazios, 29 `async void`,
   28 `Thread.Sleep` (contagem de 16/11).
7. Faxina do repositório: 27 branches remotas, betas antigas, backlog e
   `aparelhos.md` (ainda fala em 5–30 W) atualizados.

**Critérios de aceite**

- Números de antes e depois registrados em `docs/testing/`.
- Nenhuma regressão de controle, Quick Settings ou TDP numa rodada de beta.
- Nenhum arquivo de tela acima de ~1.200 linhas sem motivo anotado.

### 0.29 — Receitas por jogo

**Objetivo:** o diferencial central do projeto: cada jogo abre com a
configuração escolhida e o aparelho volta ao padrão ao fechar. A meta de FPS e o
TDP aprendido por jogo (0.27) já são o começo.

**Passo a passo**

1. Receita local por jogo com o que já é verificável: TDP (perfil, watts ou Auto
   com meta), ventoinha, resolução e taxa, Lossless (ligado e multiplicador),
   preset do HUD e notas em texto para o que o launcher não controla.
2. Criar e editar na página de detalhes (botão Y) e "salvar o estado atual" pelo
   Quick Settings com o jogo aberto.
3. Aplicar ao abrir, mostrando o que foi aplicado; restaurar ao fechar, também
   depois de falha no lançamento, fechamento inesperado ou suspensão.
4. Padrão global e "ignorar a receita desta vez".
5. Receita de dock (monitor externo) só depois da individual estável.
6. A avaliar: limitador de FPS.

**Critérios de aceite**

- Receita sobrevive a reinicialização e não se mistura entre jogos.
- Aplicação e restauração registradas; erro aparece, não vira sucesso aparente.
- Repetir, cancelar ou falhar um lançamento não deixa o sistema diferente sem
  avisar.

### 0.30 — Hardware confiável e primeira execução (Windows)

**Objetivo:** fechar as sobras da "v0.19 Multi-aparelho" do plano original.

**Passo a passo**

1. **Lossless confiável:** ler o atalho configurado no próprio Lossless (hoje
   envia Ctrl+Alt+S fixo) e confirmar se o frame generation ligou antes de dizer
   que ligou.
2. **Ajudante privilegiado** ([admin-helper.md](./admin-helper.md)): o launcher
   deixa de rodar sempre como administrador; só TDP e ventoinha passam pelo
   ajudante. Jogos e lojas deixam de herdar o admin.
3. **Assistente de primeira execução:** aparelho detectado, modo, PawnIO e
   HidHide explicados e instalados com consentimento, chave do SteamGridDB,
   conta Steam, registro no Modo Xbox. Instalação do zero sem abrir o PowerShell.
4. **ROG Ally:** perfil preparado e marcado como não validado; TDP próprio só
   quando houver aparelho para testar.
5. Estudo, sem compromisso: controle virtual (ViGEm) para o Quick Settings em
   tela cheia exclusiva.

**Critérios de aceite**

- Lossless informa o estado real; sem atalho configurado, explica como resolver.
- Launcher abre sem UAC e TDP/ventoinha continuam funcionando.
- Instalação limpa concluída só pelo assistente.

### 0.31 — Diário de sessões e bateria

**Objetivo:** dados que ajudam a decidir, sem prometer precisão que o hardware
não dá. O TDP inteligente já lê o FPS pelo RTSS; aqui ele passa a ser guardado.

**Passo a passo**

1. Por sessão: jogo, duração, receita usada, FPS médio, 1% low quando houver,
   consumo médio em watts e bateria gasta.
2. Aba **Desempenho** na página do jogo, com médias só quando houver sessões
   suficientes e a fonte de cada dado indicada.
3. Previsão de bateria por jogo a partir da terceira sessão ("cerca de 1h40
   nesta receita"), com a qualidade da estimativa.

**Critérios de aceite**

- Sem RTSS ou sem sensor, o jogo abre normalmente e nada é inventado.
- Histórico local, com opção de limpar.

### 0.32 — Página do jogo e descoberta

**Objetivo:** fechar as sobras de "Detalhes 2.0" e as pendências visuais da 0.26.

**Passo a passo**

1. Página do jogo: seção **Mídia** (capturas e trailer), seção **Arquivos**
   (pasta do jogo, pasta de saves, espaço), zoom a partir da capa e animação ao
   fechar, descrição curta.
2. Card **Grátis agora** (GamerPower, com cache e falha silenciosa).
3. Fila **Jogar depois** e filtros para escolher o próximo jogo.
4. Configurações: valor como texto na linha, sem listas suspensas.
5. Ícones do Quick Settings sem emoji; cores de uso único restantes para o tema.

**Critério de aceite:** tudo navegável pelo controle em 1280×800 e 1920×1200,
com prints aprovados depois da beta.

### 0.33 — Backup e diagnóstico

**Objetivo:** facilitar reinstalação e suporte sem conta nem serviço remoto.

1. Exportar e importar configurações, temas, receitas e referências de artes.
   Não copiar jogos nem credenciais por padrão; mostrar o conteúdo antes de
   restaurar.
2. Diagnóstico guiado na Central de Ajuda: sintoma, verificação, ação reversível.
3. Revisar logs e relatórios para tirar dados sensíveis antes de exportar.

**Critério de aceite:** arquivo inválido não apaga configurações existentes;
credenciais não são exportadas sem pedido explícito.

## 1.0 — Primeira versão estável pública

A 1.0 não significa que o launcher nunca mais terá versões. Significa que há
uma base estável, documentada e segura para o público; melhorias e novas
funcionalidades continuam depois dela.

**Checklist obrigatório**

- Receitas por jogo implementadas e aprovadas nos aparelhos oficialmente
  suportados.
- Interface em inglês revisada, com textos separados do código e idioma padrão
  definido.
- Executáveis Windows assinados e processo de distribuição/verificação
  documentado. Avaliar publicação no WinGet depois de a identidade e os pacotes
  oficiais estarem estabelecidos.
- Instalação, atualização, beta → estável, desinstalação e recuperação testadas
  numa instalação limpa.
- Linux declarado como experimental e fora do suporte oficial da 1.0.
- Documentação de primeiros passos, aparelhos, permissões, limitações, perguntas
  frequentes, privacidade e remoção de componentes de terceiros.
- Build e testes automatizados verdes; sem falhas conhecidas críticas; relatório
  de erro sem segredos ou identificadores desnecessários.
- Testes práticos de controle, resolução, escala e suspensão nos aparelhos
  suportados; período de beta sem regressões bloqueadoras.
- Pessoa que não participou do desenvolvimento consegue instalar, adicionar/
  detectar jogos, configurar o launcher e reportar um problema seguindo apenas
  a documentação.

Se um critério depender de aparelho, certificado ou acesso externo ainda
indisponível, a 1.0 deve esperar ou o suporte correspondente deve permanecer
explicitamente fora do escopo oficial.

## Depois da 1.0

Estas ideias não bloqueiam a primeira versão estável:

- **Linux/Bazzite** (decisão de 16/11/2026: fica de lado até a 1.0; o build e os
  providers continuam compilando, sem trabalho novo). Plano guardado abaixo.
- Compatibilidade comunitária: relatos e catálogo de receitas por jogo e aparelho.
- Laboratório A/B de receitas, quando o diário de sessões for confiável.
- Orçamento de bateria ("preciso de 2 horas"), sobre o TDP inteligente e o diário.
- Central de saves com sincronização, replay instantâneo, modo conforto noturno,
  painel no celular, agenda do Game Pass, alertas de saúde do aparelho.
- Extensões/plugins, depois de estabilizar APIs e modelo de permissões.

### Linux integrado ao ecossistema Bazzite (plano guardado)

**Objetivo:** transformar o suporte Linux de “compila e tem providers” em uma
experiência validada, que trabalha junto às ferramentas já instaladas.

**Passo a passo**

1. Fechar uma matriz de teste por distro, aparelho, sessão gráfica e ferramentas
   disponíveis; começar por Bazzite/handheld e registrar o que foi realmente
   testado.
2. Fazer o Quick Settings controlar **MangoHud** de verdade — habilitar/desabilitar
   e escolher opções seguras de exibição — sem instalar ou substituir o MangoHud.
   Se ele não estiver disponível, mostrar como ativá-lo/instalá-lo conforme a
   distro, sem falhar em silêncio.
3. Detectar quem controla o TDP/perfil de energia (por exemplo Steam,
   OpenGamepadUI/PowerStation ou outro serviço reconhecido). Exibir a origem do
   controle e evitar escrita concorrente. Não ativar `ryzenadj` junto com outro
   gestor sem uma forma comprovada de evitar conflito.
4. Adaptar janela, fullscreen, resolução e retorno ao jogo quando o launcher
   estiver em Gamescope/Wayland; respeitar o compositor e os serviços da sessão.
5. Provar o funcionamento de rede, gamepad, brilho, TDP, suspensão e HUD em
   aparelho real; publicar limitações por modelo na matriz de aparelhos.
6. Investigar InputPlumber e Handheld Daemon: verificar APIs, disponibilidade,
   licença, permissões e possíveis conflitos. Só implementar um adaptador
   opcional se houver um caso de uso que os providers existentes não resolvam.
   Não tornar um daemon externo obrigatório para abrir o launcher.

**Critérios de aceite**

- Pacote Linux instalável e testado no handheld-alvo, com instruções reproduzíveis.
- HUD funcional quando MangoHud está presente e estado claro quando não está.
- Não há escrita concorrente de TDP conhecida nem dependência silenciosa de
  permissões administrativas.
- Sessão Gamescope/Wayland, controles e suspensão são testados e documentados.
- Recursos não validados aparecem como indisponíveis ou não validados, sem
  alegar suporte confirmado.

## Manutenção contínua entre marcos

- Continuar reduzindo `catch` vazios e `async void` fora de eventos da interface;
  registrar contexto e erros sem esconder falhas.
- Manter testes do Core, build Windows/Linux e workflows de release.
- Confirmar cada release beta em hardware antes de promovê-la para estável.
- Manter [aparelhos.md](./aparelhos.md), [CHANGELOG.md](../CHANGELOG.md) e
  `release-notes/` alinhados com o que foi realmente lançado.
- Configuração de proteção de branches, labels e milestones do GitHub precisa ser
  verificada no repositório remoto; a existência dos templates locais não prova
  que esses controles já estejam ativos.

## O que queremos que o launcher seja

Uma biblioteca de jogos com personalidade, pensada para quem joga num portátil:
visual expressivo sem excesso, informação certa no momento certo e ajustes de
hardware que explicam o que estão fazendo. O sucesso não é ter mais botões que
os outros sistemas; é integrar o que já funciona, resolver os espaços entre
essas ferramentas e tornar o uso claro, confiável e reconhecível como Handheld
Launcher.
