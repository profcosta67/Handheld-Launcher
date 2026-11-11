# Roadmap do Handheld Launcher — da 0.25 à 1.0

> Plano de produto atualizado em 11/11/2026. As versões abaixo são marcos
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

O repositório está na versão estável **0.25.8**. Já existem biblioteca multi-loja, layouts e temas,
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

1. Quick Settings dentro do jogo: o painel não recebia o controle (abre sem
   foco e o XInput não chega a quem está em segundo plano). Na 0.25.9 o
   ajudante passa os botões ao painel; falta validar no Go 2 e no Ally.
2. Camada esbranquiçada ao abrir o QS sobre o jogo: investigar (efeito
   Acrílico do painel, perfil de cor Vívido, HDR, congelamento do jogo).
3. Administrador: medir no log quem abre o launcher e em que modo, e decidir
   a proposta do ajudante privilegiado ([admin-helper.md](./admin-helper.md)).
4. Processo: `beta/**` com CI, branch do beta mesclado no master depois de
   cada oficial, VERSION do master sempre acima do último lançado.

**Critérios de aceite:** QS navegável e fechável pelo controle dentro de jogo
em tela cheia sem bordas nos aparelhos suportados; sem camada branca; log
mostra modo de abertura em todo início.

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

### 0.27 — Linux integrado ao ecossistema Bazzite

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

### 0.28 — Receitas por jogo

**Objetivo:** entregar o diferencial central do projeto: cada jogo abrir com o
perfil que o usuário escolheu, sem tornar mudanças de hardware imprevisíveis.

**Passo a passo**

1. Definir e persistir uma receita local por jogo. Na primeira etapa, incluir
   somente configurações já suportadas e verificáveis no aparelho: TDP/preset,
   resolução e taxa quando aplicáveis, ventoinha quando suportada e opções de
   HUD/Lossless onde houver integração real.
2. Criar/editar a receita nos detalhes do jogo e permitir salvar o estado atual
   do Quick Settings para o jogo ativo.
3. Ao iniciar, aplicar a receita depois que o jogo e o dispositivo estiverem
   identificados; mostrar quais itens foram aplicados e quais não são suportados.
4. Ao encerrar, restaurar o estado anterior de forma confiável, inclusive após
   falha no lançamento, fechamento inesperado ou suspensão.
5. Manter um padrão global e uma ação simples para ignorar a receita numa
   execução; não alterar opções gráficas internas do jogo automaticamente.
6. Adicionar receitas por aparelho/dock apenas depois de a receita individual
   estar estável e de haver sinal confiável para detectar a tela externa.

**Critérios de aceite**

- Receita sobrevive a reinicialização e não se mistura entre jogos da mesma
  plataforma.
- Aplicação e restauração são registradas; erros ficam visíveis e não são
  convertidos em sucesso aparente.
- TDP respeita os limites do perfil validado; função não suportada não é
  simulada.
- Repetir, cancelar ou falhar um lançamento não deixa o sistema num estado
  diferente sem informar o usuário.

### 0.29 — Desempenho e histórico que ajudam a decidir

**Objetivo:** evoluir o diário de sessões para dar contexto útil sobre
desempenho e autonomia, sem prometer precisão que o hardware não oferece.

**Passo a passo**

1. Unificar metadados da sessão: jogo, duração, receita usada, dispositivo e
   fonte dos dados.
2. Consumir métricas de uma fonte disponível e explicitamente identificada —
   por exemplo MangoHud no Linux ou a fonte compatível já configurada no Windows.
   Não inventar ou estimar FPS quando a coleta falhar.
3. Mostrar histórico simples por jogo, com médias e tendência somente quando
   houver amostras suficientes.
4. Estimar duração de bateria por jogo apenas quando houver sessões e leitura de
   bateria suficientes; mostrar intervalo/qualidade da estimativa e permitir
   ignorá-la.
5. Avaliar logs de FPS e métricas avançadas (como 1% low) como opcionais,
   considerando custo de bateria, compatibilidade e retenção local.

**Critérios de aceite**

- Usuário consegue ver de onde vêm os dados e quantas sessões sustentam cada
  resumo.
- Ausência de ferramenta ou sensor não bloqueia o jogo nem produz dados falsos.
- Histórico permanece local, com opção de limpar e sem incluir identificadores
  pessoais no relatório.

### 0.30 — Preparação para uso cotidiano e migração

**Objetivo:** facilitar reinstalação, suporte e descoberta da biblioteca sem
acrescentar dependência de conta ou serviço remoto.

**Passo a passo**

1. Criar exportação/importação de configurações, temas, preferências e referências
   de biblioteca/artes. Não copiar jogos nem credenciais por padrão.
2. Exibir o conteúdo do backup antes de restaurar; oferecer mesclagem ou
   substituição com confirmação clara e permitir desfazer quando possível.
3. Transformar verificações existentes da Central de Ajuda em diagnóstico
   guiado: descrever sintoma, verificar condições e sugerir ação reversível,
   sem executar alterações de driver ou sistema sem consentimento.
4. Adicionar uma fila pessoal “Jogar depois” e filtros úteis para decidir o
   próximo jogo, sem impor recomendações automáticas ou coleta na nuvem.
5. Revisar logs e relatórios locais para garantir mensagens úteis e remover
   dados sensíveis antes de qualquer exportação.

**Critérios de aceite**

- Backup é legível/validado antes de restaurar; arquivos inválidos não apagam
  configurações existentes.
- Credenciais e tokens não são exportados sem pedido explícito.
- Diagnóstico explica o que verificou e o usuário mantém controle sobre ações.
- Fila de jogos funciona offline e com navegação por controle.

### 0.31 — Compatibilidade comunitária (piloto, se houver capacidade)

**Objetivo:** ajudar usuários a descobrir o que foi testado em cada combinação
de aparelho e jogo, sem lançar uma plataforma social antes de haver processo de
privacidade e moderação.

**Passo a passo**

1. Definir um formato portátil de relato: aparelho, jogo, recurso, versão do
   launcher, resultado e notas opcionais.
2. Permitir exportar e importar relatos manualmente; revisar anonimização e
   conteúdo antes de compartilhar.
3. Experimentar catálogo público curado ou colaboração por repositório, com
   política de validade, atualização e remoção.
4. Só então decidir se vale a pena adicionar serviço online, conta ou envio
   direto pelo app.

**Critério de aceite:** a função é útil sem conta; nenhum dado é enviado
automaticamente; os relatos distinguem “funcionou para alguém” de “validado pela
equipe”.

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
- Linux com escopo de suporte explicitamente declarado; recurso experimental
  não aparece como validado.
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

- Catálogo comunitário de receitas e relatos de compatibilidade.
- Laboratório A/B de receitas, somente quando a coleta de métricas for confiável.
- Orçamento de bateria e ajuste automático de TDP por meta de FPS, com limites
  seguros e validação por modelo.
- Extensões/plugins, depois de estabilizar APIs e modelo de permissões.
- Painel remoto no celular, replay instantâneo e agenda de jogos que saem do
  Game Pass, após avaliar manutenção, privacidade e fonte de dados.

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
