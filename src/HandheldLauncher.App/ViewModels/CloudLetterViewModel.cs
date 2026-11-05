namespace HandheldLauncher.App.ViewModels;

/// <summary>v0.18.3: um botão da faixa de letras da aba Nuvem ("Todos", "#", "A"…"Z").</summary>
public sealed class CloudLetterViewModel : ViewModelBase
{
    public CloudLetterViewModel(string label, string? value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    /// <summary>null = "Todos" (sem filtro de letra).</summary>
    public string? Value { get; }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }

    private bool _isCursor;
    public bool IsCursor { get => _isCursor; set => SetField(ref _isCursor, value); }
}
