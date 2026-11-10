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

/// <summary>
/// v0.25.2: contrato OPCIONAL para layouts com ecrã de lançamento próprio — o
/// Cartoon abre um popup estilo Steam Deck ao escolher um jogo. A MainWindow
/// só consulta isto quando o layout implementa a interface, por isso os
/// outros três layouts não mudam de comportamento.
/// </summary>
public interface ILaunchScreenHost
{
    bool IsLaunchScreenOpen { get; }
    void OpenLaunchScreen();
    void CloseLaunchScreen();
}