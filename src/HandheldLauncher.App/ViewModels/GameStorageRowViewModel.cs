using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// v0.18 "Gerenciador de armazenamento": uma linha da lista de jogos.
/// SizeText começa em "Calculando..." e é preenchido em segundo plano.
/// v0.18 (2ª leva): selecionável pelo controle + rótulo de como mover.
/// </summary>
public sealed class GameStorageRowViewModel : ViewModelBase
{
    public string Title { get; init; } = "";
    public string? InstallDirectory { get; init; }
    public string SourceLabel { get; init; } = "";
    public GameEntry? Game { get; init; }

    /// <summary>"Mover pela Steam" / "Mover pelo launcher" / "Não move por aqui"...</summary>
    public string MoveLabel { get; init; } = "";

    /// <summary>Pasta medida/mostrada é a do .exe (jogo manual), não uma pasta de instalação de loja.</summary>
    public bool IsExecutableFolder { get; init; }

    public long SizeBytes { get; set; }

    private string _sizeText = "Calculando...";
    public string SizeText { get => _sizeText; set => SetField(ref _sizeText, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
}
