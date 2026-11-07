using Avalonia.Controls;

namespace HandheldLauncher.App.Views.Layouts;

public interface ILibraryLayout
{
    ItemsControl GamesItemsControl { get; }
    ScrollViewer GameRowScrollViewer { get; }
    Image HeroImage { get; }
    Image HeroBackImage { get; }
    bool SupportsVerticalGrid { get; }
    bool CentersSelection { get; }
    CloudGridView CloudGrid { get; }

    /// <summary>v0.21: alterna entre fileira horizontal e grade vertical, ajustando o container externo.</summary>
    void SetVertical(bool vertical);
}