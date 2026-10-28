namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// One row in the per-tile context menu opened with the Start button.
/// Deliberately tiny — just a label and whether it's the highlighted row,
/// since the small, fixed action list (v1: re-fetch art, open game) doesn't
/// need anything fancier yet.
/// </summary>
public sealed class ContextMenuItemViewModel : ViewModelBase
{
    public ContextMenuItemViewModel(string label)
    {
        Label = label;
    }

    public string Label { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
