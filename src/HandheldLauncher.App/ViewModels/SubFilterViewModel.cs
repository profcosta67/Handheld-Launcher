using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Uma caixinha da fileira de subfiltros das abas "Computador" e
/// "Emuladores" (v0.9.18, pedido do usuário: "esses subfiltro novos
/// pensei em ser algo que marca e desmarca sabe por exemplo pra poder
/// deixar mais de um selecionado").
///
/// <para>
/// Diferente das abas de cima (uma só ativa por vez, ver
/// MainViewModel.LibraryFilter), aqui cada caixinha marca/desmarca
/// independente e vale a UNIÃO das marcadas. Regra combinada com o
/// usuário: <b>nenhuma marcada = mostra tudo</b> daquela aba, em vez de
/// tela vazia — assim entrar numa aba nunca esconde jogo nenhum sem o
/// usuário ter pedido.
/// </para>
///
/// <para>
/// O predicado fica aqui dentro (<see cref="Matches"/>) em vez de num
/// switch gigante na MainViewModel justamente pra "Emuladores" crescer
/// sem mexer em lógica nenhuma: quando entrar Ryujinx/PCSX2, é só
/// adicionar mais um item na lista construída por
/// MainViewModel.RebuildSubFilters.
/// </para>
///
/// <para>
/// <see cref="IsChecked"/> é o estado marcado/desmarcado (vale pro
/// filtro). <see cref="IsCursor"/> é só onde o CONTROLE está parado
/// dentro da fileira — são coisas separadas de propósito: no controle
/// você precisa poder passar por cima de uma caixinha sem marcá-la, e
/// precisa ver onde está antes de apertar A. No mouse o cursor não
/// importa (clicar já marca direto).
/// </para>
/// </summary>
public sealed class SubFilterViewModel : ViewModelBase
{
    private readonly Func<GameEntry, bool> _matches;

    public SubFilterViewModel(string label, Func<GameEntry, bool> matches)
    {
        Label = label;
        _matches = matches;
    }

    public string Label { get; }

    public bool Matches(GameEntry game) => _matches(game);

    private bool _isChecked;

    /// <summary>Marcada pelo usuário — entra na união de filtros ativos.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    private bool _isCursor;

    /// <summary>Onde o controle/teclado está parado na fileira (só destaque visual, não filtra nada).</summary>
    public bool IsCursor
    {
        get => _isCursor;
        set => SetField(ref _isCursor, value);
    }
}
