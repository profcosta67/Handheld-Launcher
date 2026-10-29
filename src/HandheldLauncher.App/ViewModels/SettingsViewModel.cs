using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Backs the "Configurações" dialog (SettingsWindow) — pedido do usuário
/// depois de se perder tentando achar onde colar a Steam Web API key
/// ("seria possível criar uma tela de configuração pelo próprio Launcher?
/// Acho que ficaria mais fácil"). Deliberadamente burra, igual
/// AddGameViewModel: só campos de texto + CanSave/SavedMessage — toda a
/// leitura/escrita de AppSettingsStore e o Close() do diálogo ficam no
/// code-behind da Window, não aqui.
///
/// Edita os mesmos campos que até agora só existiam como hand-edit do
/// settings.json (SteamGridDbApiKey, SteamWebApiKey, SteamId64,
/// XboxScanFolders). IMPORTANTE: salvar aqui NÃO reconfigura os serviços
/// já construídos nesta execução (LibraryService, SteamOwnedGamesClient,
/// ArtworkService etc. recebem os valores por CÓPIA no App.axaml.cs, uma
/// vez só, na abertura do app) — precisa reiniciar o launcher pra valer.
/// Não tentei fazer hot-reload disso porque ir além (destruir/reconstruir
/// os serviços em runtime) é bem mais arriscado de acertar sem conseguir
/// compilar/testar aqui, pra um ganho pequeno (economizar um reinício).
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    // v0.9.5: a tela principal de Configurações não mostra mais as caixas
    // de chave/ID direto (pedido do usuário: "gravar a tela de
    // configuração mostrando o que tem pra configurar mas não queria
    // exibir os códigos") — só o status (Has*KeyText abaixo). As caixas de
    // verdade (ainda mascaradas, ver AccountsWindow) ficam atrás do botão
    // "Gerenciar contas e chaves de API". Por isso os três setters agora
    // também notificam o Has* correspondente.

    private string _steamGridDbApiKey = string.Empty;
    public string SteamGridDbApiKey
    {
        get => _steamGridDbApiKey;
        set
        {
            if (SetField(ref _steamGridDbApiKey, value))
            {
                OnPropertyChanged(nameof(HasSteamGridDbKey));
                OnPropertyChanged(nameof(SteamGridDbStatusText));
            }
        }
    }

    public bool HasSteamGridDbKey => !string.IsNullOrWhiteSpace(_steamGridDbApiKey);

    /// <summary>"configurado ✓"/"não configurado" — pra tela principal mostrar status sem o valor da chave.</summary>
    public string SteamGridDbStatusText => HasSteamGridDbKey ? "configurado ✓" : "não configurado";

    private string _steamWebApiKey = string.Empty;
    public string SteamWebApiKey
    {
        get => _steamWebApiKey;
        set
        {
            if (SetField(ref _steamWebApiKey, value))
            {
                OnPropertyChanged(nameof(HasSteamWebApiKey));
                OnPropertyChanged(nameof(SteamWebApiStatusText));
            }
        }
    }

    public bool HasSteamWebApiKey => !string.IsNullOrWhiteSpace(_steamWebApiKey);

    public string SteamWebApiStatusText => HasSteamWebApiKey ? "configurado ✓" : "não configurado";

    private string _steamId64 = string.Empty;
    public string SteamId64
    {
        get => _steamId64;
        set
        {
            if (SetField(ref _steamId64, value))
            {
                OnPropertyChanged(nameof(HasSteamId64));
                OnPropertyChanged(nameof(SteamIdStatusText));
            }
        }
    }

    public bool HasSteamId64 => !string.IsNullOrWhiteSpace(_steamId64);

    private string? _steamPersonaName;

    /// <summary>
    /// Status pra tela PRINCIPAL de Configurações: mostra o apelido da
    /// conta detectada (não é segredo) mas NUNCA o número do SteamID64 —
    /// "esconder o número" foi pedido explícito do usuário. O número de
    /// verdade só aparece dentro de AccountsWindow (e mesmo lá, mascarado).
    /// </summary>
    public string SteamIdStatusText => !HasSteamId64
        ? "não configurado"
        : string.IsNullOrWhiteSpace(_steamPersonaName)
            ? "configurado ✓"
            : $"{_steamPersonaName} (configurado ✓)";

    private string _xboxScanFoldersText = string.Empty;

    /// <summary>Uma pasta por linha na caixa de texto — convertido de/pra AppSettings.XboxScanFolders (List&lt;string&gt;) ao carregar/salvar.</summary>
    public string XboxScanFoldersText
    {
        get => _xboxScanFoldersText;
        set => SetField(ref _xboxScanFoldersText, value);
    }

    private string _edenExecutablePath = string.Empty;

    /// <summary>
    /// v0.9.16, aba "Emuladores": caminho do `eden.exe`. Nasce preenchido
    /// quando a detecção automática achou (ver EdenInstallationDetector,
    /// que roda em App.axaml.cs e já grava em settings.json) — o botão
    /// "Procurar eden.exe…" da tela escreve aqui também.
    /// </summary>
    public string EdenExecutablePath
    {
        get => _edenExecutablePath;
        set
        {
            if (SetField(ref _edenExecutablePath, value))
            {
                OnPropertyChanged(nameof(EdenStatusText));
            }
        }
    }

    /// <summary>
    /// Texto de status abaixo do campo — serve de retorno visual tanto pra
    /// detecção automática ("achei aqui") quanto pra um caminho digitado
    /// errado (que senão só falharia silenciosamente na hora de escanear,
    /// sem o usuário entender por que nenhum jogo apareceu).
    /// </summary>
    public string EdenStatusText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_edenExecutablePath))
            {
                return "Não configurado — nenhum jogo do Eden vai aparecer na biblioteca.";
            }

            try
            {
                return File.Exists(_edenExecutablePath)
                    ? "Encontrado ✓"
                    : "Esse arquivo não existe — confira o caminho.";
            }
            catch
            {
                return "Caminho inválido — confira.";
            }
        }
    }

    private string _edenRomFoldersText = string.Empty;

    /// <summary>
    /// Uma pasta por linha — convertido de/pra AppSettings.EdenRomFolders
    /// ao carregar/salvar, igual XboxScanFoldersText acima. Escaneadas
    /// recursivamente (ver EdenLibraryScanner).
    /// </summary>
    public string EdenRomFoldersText
    {
        get => _edenRomFoldersText;
        set => SetField(ref _edenRomFoldersText, value);
    }

    private string _edenScanResultText = string.Empty;

    /// <summary>
    /// v0.9.17: resultado do botão "Testar agora" (ver
    /// SettingsWindow.OnTestEdenScanClick). Existe porque, na v0.9.16, a
    /// única forma de saber se a pasta configurada tinha funcionado era
    /// salvar, reiniciar e olhar a biblioteca — e quando o resultado veio
    /// errado (1 jogo de 18 arquivos), não havia nenhuma pista do porquê.
    /// </summary>
    public string EdenScanResultText
    {
        get => _edenScanResultText;
        set
        {
            if (SetField(ref _edenScanResultText, value))
            {
                OnPropertyChanged(nameof(HasEdenScanResult));
            }
        }
    }

    public bool HasEdenScanResult => !string.IsNullOrWhiteSpace(_edenScanResultText);

    private bool _isHandheldMode;

    /// <summary>
    /// true = modo Handheld (tela cheia, abre sozinho no login, reabre se
    /// fechar); false = modo Desktop (janela normal, nada automático, "Sair"
    /// fecha de verdade). Substitui as caixinhas separadas de KioskMode/
    /// AutoRestart — as duas sempre andam juntas.
    /// </summary>
    public bool IsHandheldMode
    {
        get => _isHandheldMode;
        set
        {
            if (SetField(ref _isHandheldMode, value))
            {
                OnPropertyChanged(nameof(IsDesktopMode));
            }
        }
    }

    public bool IsDesktopMode
    {
        get => !_isHandheldMode;
        set => IsHandheldMode = !value;
    }

    private string _versionText = string.Empty;

    /// <summary>"Handheld Launcher v0.3.5", preenchido pelo MainWindow ao abrir.</summary>
    public string VersionText
    {
        get => _versionText;
        set => SetField(ref _versionText, value);
    }

    private bool _updatesAvailable;

    /// <summary>Instalado pelo Setup.exe — só então "Verificar atualizações" faz sentido.</summary>
    public bool UpdatesAvailable
    {
        get => _updatesAvailable;
        set => SetField(ref _updatesAvailable, value);
    }

    private string _updateResultText = string.Empty;

    public string UpdateResultText
    {
        get => _updateResultText;
        set
        {
            if (SetField(ref _updateResultText, value))
            {
                OnPropertyChanged(nameof(HasUpdateResult));
            }
        }
    }

    public bool HasUpdateResult => !string.IsNullOrEmpty(_updateResultText);

    // v0.9.14: OverlayPresetIndex/OverlayStatusText (ComboBox "Overlay de
    // desempenho") saíram daqui — pedido do usuário: esse ComboBox era
    // repetido com a seleção de preset que já existe no Menu Global → HUD
    // do próprio launcher (marcador ●/○ + status do RTSS, ver
    // MainViewModel.BuildGlobalMenu/GlobalMenuPage.Hud), então "retirar
    // esse do preferências". Nada de persistência se perde: aquele menu já
    // grava em AppSettings.OverlayPreset por conta própria.

    private int _categoryIndex;

    /// <summary>
    /// v0.9.14: aba ativa desta tela — pedido do usuário pra separar em
    /// tópicos porque a tela única "encheu". Mesmo padrão visual das abas
    /// de filtro da biblioteca (Button.pill.filterTab + Classes.active, ver
    /// MainWindow.axaml) em vez de um TabControl de verdade do Avalonia —
    /// assim reaproveita o mesmo estilo/cor do resto do app sem precisar de
    /// nenhum estilo novo pro chrome padrão do FluentTheme. Resetada pra 0
    /// a cada abertura da tela (ver LoadFrom).
    /// <para>
    /// v0.9.15, pedido do usuário ("Aparencias / Diretorios / Modo de Uso /
    /// Contas (deixa por ultimo)"): ordem trocada (era 0 Contas/1 Diretórios/
    /// 2 Modo/3 Aparência até a v0.9.14). Só a ORDEM/posição mudou — os
    /// nomes das propriedades Is*Active abaixo continuam pelos nomes das
    /// categorias, não pelo índice, de propósito (pra não confundir quem
    /// ler o .axaml).
    /// </para>
    /// <para>
    /// v0.9.16, pedido do usuário ("criar uma aba com nome 'Emuladores'
    /// para possiveis integrações futuras"): entrou uma 5ª aba, entre
    /// Diretórios e Modo de uso — as duas abas "onde as coisas estão" ficam
    /// juntas, e Contas segue sendo a última (pedido explícito da v0.9.15).
    /// Ordem atual: 0 Aparência, 1 Diretórios, 2 Emuladores, 3 Modo de uso,
    /// 4 Contas. O rótulo da última virou só "Contas" (era "Contas e
    /// Chaves") pra caber 5 abas na largura da janela.
    /// </para>
    /// </summary>
    public int CategoryIndex
    {
        get => _categoryIndex;
        set
        {
            if (SetField(ref _categoryIndex, value))
            {
                OnPropertyChanged(nameof(IsCategoryAppearanceActive));
                OnPropertyChanged(nameof(IsCategoryFoldersActive));
                OnPropertyChanged(nameof(IsCategoryEmulatorsActive));
                OnPropertyChanged(nameof(IsCategoryModeActive));
                OnPropertyChanged(nameof(IsCategoryAccountsActive));
            }
        }
    }

    public bool IsCategoryAppearanceActive => _categoryIndex == 0;
    public bool IsCategoryFoldersActive => _categoryIndex == 1;
    public bool IsCategoryEmulatorsActive => _categoryIndex == 2;
    public bool IsCategoryModeActive => _categoryIndex == 3;
    public bool IsCategoryAccountsActive => _categoryIndex == 4;

    private int _fontIndex;

    /// <summary>0 Moderna (Segoe UI), 1 Divertida (Comic Sans).</summary>
    public int FontIndex { get => _fontIndex; set => SetField(ref _fontIndex, value); }

    private string _steamAccountText = string.Empty;

    /// <summary>"Conta Steam deste PC: Fulano (7656...)" ou aviso de não encontrada.</summary>
    public string SteamAccountText
    {
        get => _steamAccountText;
        set => SetField(ref _steamAccountText, value);
    }

    /// <summary>Botão "Usar conta da Steam deste PC" (dentro de AccountsWindow) — preenche o SteamID64.</summary>
    public void UseDetectedSteamAccount()
    {
        var account = SteamAccountDetector.FindMostRecent();
        if (account is null)
        {
            SteamAccountText = "Nenhuma conta Steam encontrada neste PC (abra e entre na Steam uma vez).";
            return;
        }

        _steamPersonaName = account.PersonaName;
        SteamId64 = account.SteamId64;
        SteamAccountText = $"Conta Steam deste PC: {account.PersonaName} ({account.SteamId64})";
        OnPropertyChanged(nameof(SteamIdStatusText));
    }

    private int _fontSizeIndex = 1;
    public int FontSizeIndex { get => _fontSizeIndex; set => SetField(ref _fontSizeIndex, value); }

    private int _themeIndex;
    public int ThemeIndex { get => _themeIndex; set => SetField(ref _themeIndex, value); }

    private int _tileSizeIndex = 1;
    public int TileSizeIndex { get => _tileSizeIndex; set => SetField(ref _tileSizeIndex, value); }

    private int _layoutIndex;

    /// <summary>0 = fileira horizontal, 1 = grade vertical.</summary>
    public int LayoutIndex { get => _layoutIndex; set => SetField(ref _layoutIndex, value); }

    private bool _animations = true;
    public bool Animations { get => _animations; set => SetField(ref _animations, value); }

    private bool _soundEffects = true;

    /// <summary>"Sons de interação" (v0.9.10) — Platform.UiSounds.Enabled espelha isto ao vivo (ver SettingsWindow.axaml.cs).</summary>
    public bool SoundEffects { get => _soundEffects; set => SetField(ref _soundEffects, value); }

    private int _soundThemeIndex;

    /// <summary>"Estilo de som" (v0.9.11) — índice em Platform.UiSounds.PackNames.</summary>
    public int SoundThemeIndex { get => _soundThemeIndex; set => SetField(ref _soundThemeIndex, value); }

    private double _soundVolume = 70;

    /// <summary>
    /// "Volume dos sons" (v0.9.13, 0-100) — espelha Platform.UiSounds.Volume
    /// (lá é int; aqui é double de propósito, pra bater exatamente com o
    /// tipo de Slider.Value e não depender de conversão implícita no
    /// binding, que não dá pra confirmar sem compilar).
    /// </summary>
    public double SoundVolume { get => _soundVolume; set => SetField(ref _soundVolume, value); }

    private int _colorModeIndex;

    /// <summary>"Modo de imagem/cor" (v0.9.13) — índice em Platform.DisplayColorManager.ModeNames.</summary>
    public int ColorModeIndex { get => _colorModeIndex; set => SetField(ref _colorModeIndex, value); }

    private string? _savedMessage;
    public string? SavedMessage
    {
        get => _savedMessage;
        private set
        {
            if (SetField(ref _savedMessage, value))
            {
                OnPropertyChanged(nameof(HasSavedMessage));
                OnPropertyChanged(nameof(FormEnabled));
            }
        }
    }

    public bool HasSavedMessage => !string.IsNullOrEmpty(SavedMessage);

    /// <summary>
    /// Desabilita o formulário por trás enquanto a telinha "Configurações
    /// salvas" está aberta (v0.9.7) — sem isso, ↑/↓ pelo controle ainda
    /// conseguiam mover o foco pra um campo escondido atrás do overlay
    /// (DialogGamepadNavigator não sabe nada sobre "tem overlay aberto",
    /// só sobre foco/visibilidade — desabilitar os controles de verdade é
    /// o jeito do próprio Avalonia tirá-los da navegação).
    /// </summary>
    public bool FormEnabled => !HasSavedMessage;

    /// <summary>Carrega os campos a partir de settings.json (via AppSettingsStore.Load), chamado quando a Window abre.</summary>
    public void LoadFrom(AppSettings settings)
    {
        SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty;
        SteamWebApiKey = settings.SteamWebApiKey ?? string.Empty;

        // Detecta ANTES de preencher SteamId64, pra SteamIdStatusText (tela
        // principal) já nascer com o apelido certo em vez de "configurado
        // ✓" genérico — ver comentário da propriedade.
        var detected = SteamAccountDetector.FindMostRecent();
        _steamPersonaName = detected?.PersonaName;
        SteamId64 = settings.SteamId64 ?? string.Empty;
        XboxScanFoldersText = string.Join(Environment.NewLine, settings.XboxScanFolders);
        EdenExecutablePath = settings.EdenExecutablePath ?? string.Empty;
        EdenRomFoldersText = string.Join(Environment.NewLine, settings.EdenRomFolders);
        IsHandheldMode = settings.KioskMode;
        SteamAccountText = detected is null
            ? "Nenhuma conta Steam encontrada neste PC."
            : $"Conta Steam deste PC: {detected.PersonaName} ({detected.SteamId64})";
        CategoryIndex = 0;
        ThemeIndex = Math.Clamp(settings.Theme, 0, HandheldLauncher.App.Platform.ThemeManager.ThemeNames.Length - 1);
        FontIndex = Math.Clamp(settings.UiFont, 0, HandheldLauncher.App.Platform.ThemeManager.Fonts.Length - 1);
        FontSizeIndex = Math.Clamp(settings.UiFontSize, 0, 2);
        TileSizeIndex = Math.Clamp(settings.TileSize, 0, 2);
        LayoutIndex = settings.VerticalLayout ? 1 : 0;
        Animations = settings.Animations;
        SoundEffects = settings.SoundEffects;
        SoundThemeIndex = Math.Clamp(settings.SoundTheme, 0, HandheldLauncher.App.Platform.UiSounds.PackNames.Length - 1);
        SoundVolume = Math.Clamp(settings.SoundVolume, 0, 100);
        ColorModeIndex = Math.Clamp(settings.ColorMode, 0, HandheldLauncher.App.Platform.DisplayColorManager.ModeNames.Length - 1);
        SavedMessage = null;
    }

    /// <summary>
    /// Botão "Salvar" — grava de volta em AppSettings e persiste via
    /// AppSettingsStore.Save (mesmo arquivo/caminho que o hand-edit sempre
    /// usou, %AppData%\HandheldLauncher\settings.json). Não fecha a tela
    /// sozinho — deixa a mensagem de confirmação visível pro usuário ler
    /// o aviso de "precisa reiniciar" antes de fechar.
    /// </summary>
    public void SaveTo(AppSettings settings)
    {
        settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey.Trim();
        settings.SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        settings.SteamId64 = string.IsNullOrWhiteSpace(SteamId64) ? null : SteamId64.Trim();
        settings.XboxScanFolders = XboxScanFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        settings.EdenExecutablePath = string.IsNullOrWhiteSpace(EdenExecutablePath) ? null : EdenExecutablePath.Trim();
        settings.EdenRomFolders = EdenRomFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        settings.KioskMode = IsHandheldMode;
        settings.AutoRestart = IsHandheldMode;
        settings.ModeChosen = true;
        settings.Theme = Math.Clamp(ThemeIndex, 0, HandheldLauncher.App.Platform.ThemeManager.ThemeNames.Length - 1);
        settings.UiFont = Math.Clamp(FontIndex, 0, HandheldLauncher.App.Platform.ThemeManager.Fonts.Length - 1);
        settings.UiFontSize = Math.Clamp(FontSizeIndex, 0, 2);
        settings.TileSize = Math.Clamp(TileSizeIndex, 0, 2);
        settings.VerticalLayout = LayoutIndex == 1;
        settings.Animations = Animations;
        settings.SoundEffects = SoundEffects;
        settings.SoundTheme = Math.Clamp(SoundThemeIndex, 0, HandheldLauncher.App.Platform.UiSounds.PackNames.Length - 1);
        settings.SoundVolume = Math.Clamp((int)Math.Round(SoundVolume), 0, 100);
        settings.ColorMode = Math.Clamp(ColorModeIndex, 0, HandheldLauncher.App.Platform.DisplayColorManager.ModeNames.Length - 1);

        AppSettingsStore.Save(settings);

        // Handheld: cria a tarefa que abre no login e reabre se fechar.
        // Desktop: remove. (Melhor esforço — ver WatchdogTask.)
        var taskOk = WatchdogTask.Apply(IsHandheldMode);

        SavedMessage = "Salvo! Aparência já vale ao fechar esta tela; chaves e modo de uso pedem reiniciar o launcher (fechar de vez e abrir de novo)."
            + (taskOk ? string.Empty : " Aviso: não consegui ajustar a abertura automática do Windows (veja debug.log).");
    }

    /// <summary>Fecha a telinha de confirmação "Configurações salvas" (botão OK, v0.9.7) — ver SettingsWindow.axaml.cs.OnSavedOkClick.</summary>
    public void DismissSavedMessage() => SavedMessage = null;
}
