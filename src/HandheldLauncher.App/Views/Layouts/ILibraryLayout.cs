using Avalonia.Controls;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// Contrato entre a MainWindow e o layout da biblioteca que está em uso
/// (v0.9.20, projeto de temas de LAYOUT — ver AppSettings.LayoutTheme).
///
/// <para>
/// <b>Por que isto existe:</b> o code-behind da MainWindow usa quatro
/// controles da área da biblioteca em ~34 lugares — a conta do scroll
/// horizontal/vertical, a navegação ↑/↓ pela posição real das tiles, a
/// troca de painel entre fileira e grade, e a transição suave do banner.
/// Quando esses controles moraram no XAML da própria janela, o Avalonia
/// gerava campos pra eles automaticamente. Agora que vivem dentro de um
/// UserControl por tema, cada layout precisa ENTREGAR os quatro, e a
/// MainWindow guarda as referências em campos com os MESMOS nomes de
/// antes — por isso aquelas ~34 chamadas seguiram compilando sem
/// alteração nenhuma.
/// </para>
///
/// <para>
/// <b>O que isso obriga de um layout novo:</b> ter uma lista de jogos
/// (ItemsControl), ela dentro de um ScrollViewer, e uma imagem de banner
/// com uma segunda imagem atrás pra transição. Não é uma limitação
/// acidental — é o mínimo que a infraestrutura compartilhada (scroll,
/// navegação por controle, fade do banner) precisa pra funcionar igual nos
/// dois temas. Um layout que não queira banner pode apontar
/// HeroImage/HeroBackImage pra duas Image escondidas; o resto continua
/// funcionando.
/// </para>
/// </summary>
internal interface ILibraryLayout
{
    /// <summary>A lista de jogos em si — navegação e scroll medem as tiles por ela.</summary>
    ItemsControl GamesItemsControl { get; }

    /// <summary>O ScrollViewer que embrulha a lista acima.</summary>
    ScrollViewer GameRowScrollViewer { get; }

    /// <summary>Banner do jogo em foco (a imagem "da frente", que recebe o fade).</summary>
    Image HeroImage { get; }

    /// <summary>Imagem de trás do banner — fica com a arte ANTERIOR enquanto a da frente aparece.</summary>
    Image HeroBackImage { get; }

    /// <summary>
    /// False quando o layout não suporta a grade vertical (
    /// AppSettings.VerticalLayout) — o layout Switch é fileira horizontal
    /// por natureza, então a MainWindow não tenta trocar o ItemsPanel dele.
    /// </summary>
    bool SupportsVerticalGrid { get; }

    /// <summary>
    /// True = a capa em foco deve ficar SEMPRE centralizada na tela, com a
    /// fileira correndo por baixo dela (a roleta do tema Switch, v0.9.21).
    /// False = traz pra área visível com o movimento mínimo, que é o
    /// comportamento do Clássico — ali a capa em foco pode ficar em
    /// qualquer ponto da fileira, e recentralizar a cada passo deixaria a
    /// tela inquieta. Ver MainWindow.ScrollSelectionIntoViewNow.
    /// </summary>
    bool CentersSelection { get; }
}
