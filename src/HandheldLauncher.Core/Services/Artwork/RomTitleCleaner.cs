using System.Text.RegularExpressions;

namespace HandheldLauncher.Core.Services.Artwork;

/// <summary>
/// v0.18.4 (bug do usuário depois da instalação limpa: "os Pokémon não acharam
/// as artes"). Jogo de emulador (Eden) usa o NOME DO ARQUIVO como título, e
/// era esse nome cru que ia como termo de busca pra SteamGridDB: por exemplo
/// "Pokemon Legends Arceus-002", "Pokemon NSP" ou
/// "Jogo [0100ABCD12340000][v0]". Com a sujeira do arquivo a busca não acha
/// nada, ou acha um resultado que não bate. Antes da instalação limpa os
/// nomes tinham sido editados à mão ("Editar"), por isso funcionava; ao
/// apagar a pasta de dados os nomes voltaram a ser os do arquivo.
///
/// Aqui só limpa o TERMO DE BUSCA. O nome mostrado na biblioteca não muda, e
/// nome editado pelo usuário é usado como está (ver ArtworkService.SearchTitleFor).
/// </summary>
public static class RomTitleCleaner
{
    // [title id], [v0], (USA), {qualquer coisa}
    private static readonly Regex Groups = new(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}", RegexOptions.Compiled);

    // "-002" de revisão no fim (mesma regra que NormalizeGameTitle já usa pra comparar).
    private static readonly Regex RevisionSuffix = new(@"\s*-\s*\d{3}\s*$", RegexOptions.Compiled);

    // Marcações de formato/plataforma/versão, SÓ no fim do nome — no meio
    // podem ser parte do título ("Nintendo Switch Sports").
    private static readonly Regex TrailingJunk = new(
        @"[\s\-]+(nsp|nsz|xci|xcz|nsw|switch|eshop|base\s+game|v\d+(\.\d+)*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static string Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var original = title.Trim();

        var value = original.Replace('_', ' ');
        // "Pokemon.Legends.Arceus" — ponto como separador só quando não há espaço nenhum.
        if (!value.Contains(' ')) value = value.Replace('.', ' ');

        value = Groups.Replace(value, " ");
        value = Spaces.Replace(value, " ").Trim();

        // Repete porque a sujeira vem empilhada: "... Switch NSP Base Game-002".
        string previous;
        do
        {
            previous = value;
            value = RevisionSuffix.Replace(value, "");
            value = TrailingJunk.Replace(value, "");
            value = value.Trim().TrimEnd('-', ' ');
        }
        while (value != previous && value.Length > 0);

        value = Spaces.Replace(value, " ").Trim();
        return value.Length > 0 ? value : original;
    }
}
