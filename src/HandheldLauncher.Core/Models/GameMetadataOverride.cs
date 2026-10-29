namespace HandheldLauncher.Core.Models;

/// <summary>
/// Everything about a game that the launcher itself owns and must survive a
/// re-scan — a fresh Steam scan rebuilds GameEntry from the manifest every
/// time, so anything we set locally (custom art, collection, playtime) has
/// to live here and get re-applied on top after each scan.
/// </summary>
public class GameMetadataOverride
{
    public string? Collection { get; set; }
    public string? GridImagePath { get; set; }
    public string? HeroImagePath { get; set; }
    public string? LogoImagePath { get; set; }
    public string? IconImagePath { get; set; }
    public string? WideGridImagePath { get; set; }
    public long PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayedUtc { get; set; }
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Nome escolhido pelo usuário (v0.9.19, pedido dele: "editar o nome do
    /// atalho do jogo, senti falta disso"). Null = usa o nome que o scanner
    /// achou.
    /// <para>
    /// Mora aqui, e não em GameEntry.Title direto, justamente porque é isto
    /// que esta classe existe pra resolver: um re-scan reconstrói o
    /// GameEntry do zero a partir do manifesto/arquivo e sobrescreveria o
    /// nome editado. Efeito colateral bom: faz o renomear funcionar pra
    /// QUALQUER entrada, inclusive jogo escaneado da Steam, não só atalho
    /// criado à mão.
    /// </para>
    /// </summary>
    public string? TitleOverride { get; set; }

    /// <summary>
    /// Marca a entrada como PROGRAMA, não jogo (v0.9.19, pedido do usuário:
    /// "incluir uma aba de programas pra adicionar Chrome, Navegador de
    /// pastas etc."). Programas aparecem só na aba "Programas" — ficam fora
    /// de Todos/Computador/Emuladores, pra não misturar utilitário com jogo
    /// na biblioteca.
    /// </summary>
    public bool IsApplication { get; set; }

    /// <summary>
    /// Cópia desta customização (v0.9.23). Existe pra matar uma classe de
    /// bug que já mordeu TRÊS vezes neste arquivo: quem precisava copiar um
    /// override fazia isso campo a campo, e todo campo novo que alguém
    /// esquecesse de acrescentar na lista era silenciosamente APAGADO do
    /// jogo quando aquele caminho rodava.
    ///
    /// <para>
    /// Histórico: aconteceu com <c>WideGridImagePath</c>, com
    /// <c>IsFavorite</c> e, na v0.9.22, com <c>TitleOverride</c>/
    /// <c>IsApplication</c> — este último reportado pelo usuário como "eu
    /// edito o nome, mas mudo a imagem e ele volta pro nome original".
    /// Buscar uma capa recriava o override sem o nome editado.
    /// </para>
    ///
    /// <para>
    /// MemberwiseClone copia TUDO, inclusive campos que ainda não existem —
    /// então campo novo nunca mais precisa ser lembrado em lugar nenhum.
    /// Cópia rasa é correta aqui porque todo campo é tipo de valor ou
    /// string (imutável).
    /// </para>
    /// </summary>
    public GameMetadataOverride Clone() => (GameMetadataOverride)MemberwiseClone();
}
