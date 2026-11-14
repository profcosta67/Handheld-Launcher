namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// One row in the per-tile context menu opened with the Start button.
/// Deliberately tiny — just a label and whether it's the highlighted row,
/// since the small, fixed action list (v1: re-fetch art, open game) doesn't
/// need anything fancier yet.
/// </summary>
public sealed class ContextMenuItemViewModel : ViewModelBase
{
    public ContextMenuItemViewModel(string label, bool isDanger = false, bool isFolder = false)
    {
        _label = label;
        IsDanger = isDanger;
        IsFolder = isFolder;
    }

    // Item que abre outra página do menu (as "pastas": Biblioteca, HUD,
    // Atualizações...). A seta "›" só aparece nestes — é o padrão de tile
    // (metade esquerda clicável + indicador à direita) do resto do launcher.
    public bool IsFolder { get; }

    // Item destrutivo/irreversível ("Sim, excluir...", "Sim, reiniciar...")
    // — pedido do usuário pra deixar a confirmação "mais chamativa" (ver
    // Theme.axaml, estilo Border.menuItem.danger). Fixo na criação, nunca
    // muda depois (ao contrário de Label), então sem setter.
    public bool IsDanger { get; }

    // Era { get; } (fixo pra sempre) — agora com setter porque o item
    // "Favoritar"/"Desfavoritar" precisa trocar de texto dependendo do jogo
    // em foco quando o menu abre (ver MainViewModel.ToggleContextMenu).
    private string _label;
    public string Label
    {
        get => _label;
        set
        {
            if (!SetField(ref _label, value)) return;
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(IconData));
            OnPropertyChanged(nameof(HasIcon));
        }
    }

    // v0.26: os rótulos nasceram com um emoji na frente ("🎨 Tema"). O menu agora mostra
    // um ícone do próprio launcher, escolhido pelo nome do item, e o texto sem o emoji.
    // Label continua igual para o resto do código.
    public string DisplayLabel => StripLeadingSymbol(_label);

    public Avalonia.Media.Geometry? IconData
    {
        get
        {
            var key = IconKeyFor(DisplayLabel);
            if (key is null) return null;
            try
            {
                return Avalonia.Application.Current is { } app
                       && app.TryFindResource(key, out var res) && res is Avalonia.Media.Geometry g ? g : null;
            }
            catch { return null; }
        }
    }

    public bool HasIcon => IconData is not null;

    internal static string StripLeadingSymbol(string? label)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;
        var space = label.IndexOf(' ');
        if (space <= 0 || space > 4) return label;
        for (var i = 0; i < space; i++)
        {
            var c = label[i];
            if (c < 0x250 && char.IsLetterOrDigit(c)) return label; // primeira "palavra" é texto de verdade
        }
        return label[(space + 1)..].TrimStart();
    }

    // Ordem importa: os mais específicos primeiro.
    private static readonly (string Word, string Icon)[] IconRules =
    {
        ("fechar launcher", "IconExit"), ("fechar o jogo", "IconStop"), ("fechar xbox", "IconStop"),
        ("desligar tela", "IconMoon"), ("reiniciar", "IconRefresh"), ("desligar", "IconPower"),
        ("quick settings", "IconGamepad"), ("biblioteca", "IconGrid"), ("hud", "IconMonitor"),
        ("tema", "IconPalette"), ("personalizar", "IconPalette"), ("configura", "IconSettings"),
        ("armazenamento", "IconStorage"), ("saúde", "IconHeart"), ("ajuda", "IconHelp"),
        ("energia", "IconPower"), ("atalhos", "IconKeyboard"), ("sobre", "IconInfo"),
        ("detalhes", "IconInfo"), ("editar", "IconEdit"), ("excluir", "IconTrash"),
        ("voltar", "IconBack"), ("cancelar", "IconClose"), ("adicionar", "IconPlus"),
        ("capa", "IconPicture"), ("banner", "IconPicture"), ("logo", "IconPicture"), ("ícone", "IconPicture"),
        ("recarregar", "IconRefresh"), ("verificar", "IconRefresh"), ("atualiza", "IconRefresh"),
        ("windows", "IconAppWindow"), ("driver", "IconSettings"),
        ("instalar", "IconDownload"), ("download", "IconDownload"), ("baixa", "IconDownload"), ("rtss", "IconDownload"),
        ("nuvem", "IconCloud"), ("favorit", "IconStar"), ("abrir jogo", "IconPlay"), ("jogar", "IconPlay"),
    };

    private static string? IconKeyFor(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var (word, icon) in IconRules)
            if (lower.Contains(word)) return icon;
        return null;
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
