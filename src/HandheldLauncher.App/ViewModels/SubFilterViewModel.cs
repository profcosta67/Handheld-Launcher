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

    public SubFilterViewModel(string label, string accentHex, string onAccentHex, Func<GameEntry, bool> matches)
    {
        Label = label;
        AccentHex = accentHex;
        OnAccentHex = onAccentHex;
        _matches = matches;
    }

    public string Label { get; }

    /// <summary>
    /// Cor da plataforma (v0.9.19, pedido do usuário: as caixinhas
    /// "ficaram um pouco escondidas", e ele pediu pra destacar "colocando
    /// elas na cor do tema, puxando nosso ícone das capas"). Usada como
    /// preenchimento quando marcada e como contorno+texto quando
    /// desmarcada — ver BackgroundHex/ForegroundHex abaixo.
    /// <para>
    /// Não é exatamente a mesma cor do selo da capa
    /// (GameTileViewModel.PlatformBadgeColorHex): lá a cor é SÓ fundo, com
    /// texto branco fixo, então tons bem escuros funcionam. Aqui a mesma
    /// cor também vira contorno e TEXTO sobre o fundo do tema, então
    /// alguns tons precisaram clarear pra continuar legíveis (o
    /// #1B2838 da Steam, por exemplo, desaparece num tema escuro).
    /// </para>
    /// </summary>
    public string AccentHex { get; }

    /// <summary>Cor do texto quando a caixinha está preenchida (marcada) — tem que contrastar com AccentHex.</summary>
    public string OnAccentHex { get; }

    public bool Matches(GameEntry game) => _matches(game);

    private bool _isChecked;

    /// <summary>Marcada pelo usuário — entra na união de filtros ativos.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value))
            {
                OnPropertyChanged(nameof(BackgroundHex));
                OnPropertyChanged(nameof(ForegroundHex));
            }
        }
    }

    /// <summary>
    /// Marcada = preenchida com a cor da plataforma. Desmarcada =
    /// transparente (só o contorno colorido aparece).
    /// <para>
    /// Por que a cor vem ligada por binding em vez de sair de um estilo
    /// <c>.marked</c> como antes: o estilo é um só pra todas as caixinhas
    /// e não tem como ele saber a cor de CADA plataforma. E como valor
    /// local vence estilo no Avalonia, não dá pra misturar os dois — se
    /// Background vem por binding, ele vence sempre, então o binding tem
    /// que cobrir os DOIS estados (é o que estas duas propriedades fazem).
    /// </para>
    /// </summary>
    public string BackgroundHex => _isChecked ? AccentHex : "#00000000";

    /// <summary>Marcada = cor de contraste sobre o preenchimento. Desmarcada = a própria cor da plataforma, como texto.</summary>
    public string ForegroundHex => _isChecked ? OnAccentHex : AccentHex;

    private bool _isCursor;

    /// <summary>Onde o controle/teclado está parado na fileira (só destaque visual, não filtra nada).</summary>
    public bool IsCursor
    {
        get => _isCursor;
        set => SetField(ref _isCursor, value);
    }
}
