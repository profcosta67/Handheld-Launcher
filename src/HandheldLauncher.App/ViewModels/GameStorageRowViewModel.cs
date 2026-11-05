namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// v0.18 "Gerenciador de armazenamento": uma linha da lista de jogos com
/// seu tamanho em disco. O tamanho é calculado em segundo plano (pode levar
/// um tempo pra jogos com muitos arquivos) — a linha nasce com "Calculando…"
/// e troca pro valor real quando terminar, sem bloquear a tela.
/// </summary>
public sealed class GameStorageRowViewModel : ViewModelBase
{
    public string Title { get; init; } = "";
    public string? InstallDirectory { get; init; }
    public string SourceLabel { get; init; } = "";

    private string _sizeText = "Calculando…";
    public string SizeText
    {
        get => _sizeText;
        set => SetField(ref _sizeText, value);
    }

    /// <summary>Em bytes — só pra ordenar a lista depois de calculada; não é exibido direto.</summary>
    public long SizeBytes { get; set; }
}
