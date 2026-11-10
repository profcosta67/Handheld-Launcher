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
        set => SetField(ref _label, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
