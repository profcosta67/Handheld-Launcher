namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// v0.18 Detalhes 2.0: itens simples pras listas da página do jogo
/// (etiquetas e capturas). Classes próprias em vez de string crua porque
/// o projeto usa compiled bindings por padrão — DataTemplate precisa de
/// um x:DataType concreto.
/// </summary>
public sealed class DetailsTagItem
{
    public required string Text { get; init; }
}

public sealed class DetailsImageItem
{
    public required string Path { get; init; }
}
