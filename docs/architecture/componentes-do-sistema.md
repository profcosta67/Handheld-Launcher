# Componentes do sistema (PawnIO, HidHide, RTSS)

Arquivos: `src/HandheldLauncher.App/Platform/SystemComponents.cs` (detecção e instalação),
`Platform/StartupComponents.cs` (o que roda ao abrir), `Views/ComponentsWindow.axaml` (a tela).

## Por que existe

Teste de 18/11, instalação limpa do Windows: o TDP não funcionava. O log repetia
`[PawnIO] Driver não encontrado` em todas as tentativas. O instalador do PawnIO vem no pacote, mas
quem o chamava era o `InstallTask.ps1`, que o Velopack roda **sem administrador** e com limite de
30 s — nunca chegava nele. E nada na interface avisava.

## Como funciona agora

O launcher abre sempre como administrador, então é ele que cuida disso. 12 s depois de abrir, em
thread de fundo (`StartupComponents`):

1. copia para o `debug.log` o resultado da tarefa de inicialização do controle;
2. cria ou remove essa tarefa conforme `WakeControllerAtBoot`;
3. com `AutoInstallComponents` ligado (padrão), instala o que falta e vem no pacote;
4. se faltar algo que precisa do usuário, abre a tela de componentes — uma vez por versão, nunca
   por cima de jogo.

| Componente | Para quê | De onde vem |
| --- | --- | --- |
| PawnIO | ler e escrever os limites de energia (sem ele o TDP não é aplicado) | `Tools\PawnIO_setup.exe`, instalado em silêncio; o serviço fica em início automático |
| HidHide | esconder o controle do jogo (Quick Settings com jogo aberto) | `Tools\HidHide_*.exe`, instalado em silêncio |
| RTSS (RivaTuner Statistics Server) | medir o FPS, desenhar o HUD e alimentar o TDP inteligente | **winget** (`Guru3D.RTSS`), só quando o usuário aperta o botão — não pode ser redistribuído no nosso pacote |

Detecção: `sc query PawnIO` (e se o serviço está rodando), a chave
`HKLM\SOFTWARE\Nefarius Software Solutions\HidHide` e o `RTSS.exe` nos dois caminhos de instalação
padrão. Os dois drivers podem pedir um reinício do Windows para valer.

Tudo é melhor esforço: cada passo vira linha `[COMPONENTES]` no `debug.log` e nenhuma falha aqui
derruba o launcher. A Central de Ajuda tem a verificação "Componentes do sistema", o botão
"🧩 Componentes" e o artigo `componentes` no manual.
