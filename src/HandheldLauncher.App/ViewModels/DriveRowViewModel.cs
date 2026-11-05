namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// v0.18 "Saúde do aparelho" / "Gerenciador de armazenamento": uma linha de
/// disco (letra, uso, % ocupado) pras duas telas novas. Classe própria em
/// vez de aninhada na janela — precisa de um tipo de nível superior pro
/// x:DataType do DataTemplate (AvaloniaUseCompiledBindingsByDefault=true
/// neste projeto) resolver certo.
/// </summary>
public sealed class DriveRowViewModel : ViewModelBase
{
    public string Label { get; init; } = "";
    public string UsageText { get; init; } = "";
    public double UsedPercent { get; init; }
}
