namespace HandheldLauncher.Core.Models;

/// <summary>
/// App-wide settings — not per-game, unlike GameMetadataOverride. Lives at
/// %AppData%\HandheldLauncher\settings.json. Edited through the
/// "Configurações" dialog (ver SettingsWindow/SettingsViewModel, aberto
/// pelo menu global) — AppSettingsStore ainda cria o arquivo com campos
/// vazios no primeiro run, caso alguém prefira editar na mão.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// From https://www.steamgriddb.com/profile/preferences/api — needed
    /// for cover-art fetching. Artwork fetching is silently skipped
    /// (not an error) whenever this is empty.
    /// </summary>
    public string? SteamGridDbApiKey { get; set; }

    /// <summary>
    /// When true, MainWindow opens fullscreen/borderless instead of the
    /// normal resizable dev window — the "modo kiosk" requested for the
    /// handheld (Legion Go 2 / ROG Ally). Deliberately does NOT replace
    /// the Windows shell: Alt+Tab, Alt+F4 and the taskbar underneath all
    /// keep working exactly as normal, so there's no way to get stuck —
    /// closing the app (or Alt+Tab'ing away) always gets back to a normal
    /// desktop. installer/Install-Launcher.ps1 sets this to true; plain
    /// `dotnet run` during development leaves it false (default) so the
    /// window stays resizable for iterating.
    /// </summary>
    public bool KioskMode { get; set; }

    /// <summary>
    /// Pastas extras pra procurar jogos do Xbox app / Game Pass (ex:
    /// "C:\XboxGames" ou "D:\WindowsApps") — pedido do usuário depois que
    /// a detecção automática não achou nada. Não existe nenhum scanner
    /// de UWP/Xbox de verdade ainda (precisaria de pacotes que este
    /// projeto não referencia), então isto é um atalho manual: cada
    /// pasta listada aqui é escaneada por XboxFolderScanner, que trata
    /// toda SUBPASTA IMEDIATA dentro dela como um jogo (pega o maior
    /// .exe lá dentro como executável — ver XboxFolderScanner pra
    /// detalhes/limitações). Hand-edit só, igual SteamGridDbApiKey
    /// acima — sem UI de configuração ainda. Lista vazia = nenhuma pasta
    /// extra, comportamento de antes (só a checkbox manual em
    /// "Adicionar jogo" cria entradas Xbox).
    /// </summary>
    public List<string> XboxScanFolders { get; set; } = new();

    /// <summary>
    /// Steam Web API key, de https://steamcommunity.com/dev/apikey —
    /// precisa dos DOIS campos (este + SteamId64 abaixo) pra aparecer a
    /// lista "na sua conta, não instalado" no painel de Downloads
    /// (pedido do usuário: "se der pra ver os jogos que tem na conta
    /// seria legal também"). Diferente do SteamGridDbApiKey acima — são
    /// serviços/keys completamente separados. Sem os dois campos
    /// preenchidos, essa lista simplesmente fica vazia com uma mensagem
    /// explicando o que falta — não é um erro.
    /// </summary>
    public string? SteamWebApiKey { get; set; }

    /// <summary>
    /// Seu SteamID64 (um número de 17 dígitos, ex: "76561198012345678") —
    /// acha o seu em https://steamid.io/ colando a URL do seu perfil
    /// Steam. Precisa estar com o perfil/"Detalhes do jogo" público pra
    /// GetOwnedGames devolver alguma coisa (ver README).
    /// </summary>
    public string? SteamId64 { get; set; }

    /// <summary>
    /// "Abrir sozinho no login e reabrir se fechar" (Tarefa Agendada do
    /// modo console). Padrão true (comportamento de handheld/console).
    /// Desligado (uso no PC): toda abertura automática encerra na hora —
    /// ver App/Platform/WatchdogGate. Abrir pelo atalho continua normal.
    /// </summary>
    public bool AutoRestart { get; set; } = true;

    /// <summary>
    /// O usuário já escolheu o modo (Desktop/Handheld) na tela de
    /// Configurações. Falso + KioskMode desligado = o launcher abre as
    /// Configurações sozinho na primeira vez pra perguntar.
    /// </summary>
    public bool ModeChosen { get; set; }

    /// <summary>
    /// HUD de desempenho estilo MangoHud (desenhado pelo RTSS por cima do
    /// jogo): 0 Desligado, 1 Resumido, 2 Médio (padrão), 3 Completo.
    /// </summary>
    public int OverlayPreset { get; set; } = 2;

    /// <summary>Tema (índice em ThemeManager.ThemeNames): 0 Escuro, 1 Claro, 2 Azul, 3 Vermelho, 4 Verde, 5 Laranja, 6 Rosa, 7 Ciano, 8 Preto (OLED), 9 Dourado, 10 Switch.</summary>
    public int Theme { get; set; }

    /// <summary>Tamanho das capas na biblioteca: 0 Pequeno, 1 Médio (padrão), 2 Grande.</summary>
    public int TileSize { get; set; } = 1;

    /// <summary>Biblioteca em grade com rolagem vertical (true) ou fileira horizontal (false, padrão).</summary>
    public bool VerticalLayout { get; set; }

    /// <summary>Animações (zoom no jogo selecionado/ao passar o mouse, rolagem suave). Padrão ligado.</summary>
    public bool Animations { get; set; } = true;

    /// <summary>Sons de interação pelo controle (navegar/confirmar/voltar — Platform/UiSounds). Padrão ligado.</summary>
    public bool SoundEffects { get; set; } = true;

    /// <summary>Estilo do som de interação (índice em Platform.UiSounds.PackNames): 0 Clássico (padrão), 1 Suave, 2 Retro, 3 Future.</summary>
    public int SoundTheme { get; set; }

    /// <summary>
    /// Volume dos sons de interação (0-100, v0.9.13) — índice espelhado em
    /// Platform.UiSounds.Volume. Padrão 70, não 100: usuário reportou
    /// "estourada" testando no volume máximo do aparelho; 70 já reduz isso
    /// sem precisar regerar os .wav (ver UiSounds pra detalhe de como a
    /// escala é aplicada, já que PlaySound não tem volume próprio).
    /// </summary>
    public int SoundVolume { get; set; } = 70;

    /// <summary>
    /// Modo de otimização de imagem/cor (índice em
    /// Platform.DisplayColorManager.ModeNames): 0 Padrão (sem ajuste,
    /// padrão), 1 Vívido, 2 OLED (contraste alto), 3 Noturno (luz quente),
    /// 4 Confortável. Via SetDeviceGammaRamp — experimental, ver os avisos
    /// no comentário de DisplayColorManager antes de mudar o padrão.
    /// </summary>
    public int ColorMode { get; set; }

    /// <summary>Fonte da interface (índice em ThemeManager.Fonts): 0 Moderna (Segoe UI, padrão), 1 Divertida, 2 Formal, 3 Gamer, 4 Terminal, 5 Arial, 6 Times New Roman, 7 Verdana, 8 Trebuchet.</summary>
    public int UiFont { get; set; }

    /// <summary>Tamanho do texto (v0.7.1): 0 Pequeno, 1 Médio (padrão), 2 Grande.</summary>
    public int UiFontSize { get; set; } = 1;

    /// <summary>
    /// v0.9.15, item antigo do roteiro ("Integração com o emulador Eden"):
    /// caminho completo pro `eden.exe` instalado (ex:
    /// "C:\Eden\eden.exe") — precisa ser preenchido à mão, igual
    /// XboxScanFolders/SteamGridDbApiKey quando foram criados (sem UI de
    /// configuração ainda). Vazio/null = EdenLibraryScanner não escaneia
    /// nada (comportamento de antes, nenhum jogo do Eden aparece).
    /// <para>
    /// Por quê precisa disso e os outros scanners (Epic/GOG/Ubisoft/EA)
    /// não: aqueles abrem o jogo pelo LAUNCHER OFICIAL de cada loja (URI
    /// própria ou .exe do jogo em si) — o Eden não tem um "protocolo" de
    /// URI pra abrir um jogo específico, então quem abre é o launcher
    /// (como `eden.exe -g "caminho\do\jogo.nsp"`, ver EdenLibraryScanner),
    /// e pra isso ele precisa saber onde o `eden.exe` está. Diferente de
    /// XboxFolderScanner (lê pastas configuradas aqui MESMO pra achar
    /// jogos), EdenLibraryScanner lê as pastas de jogo que já estão
    /// configuradas DENTRO do próprio Eden (qt-config.ini) — só o
    /// caminho do `eden.exe` precisa ser dito aqui.
    /// </para>
    /// </summary>
    public string? EdenExecutablePath { get; set; }
}
