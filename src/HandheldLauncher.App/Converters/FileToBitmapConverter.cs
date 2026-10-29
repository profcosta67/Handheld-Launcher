using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace HandheldLauncher.App.Converters;

/// <summary>
/// Binds a file-path string (GameEntry.GridImagePath etc.) straight to an
/// Image.Source. Missing/invalid paths just produce no image instead of
/// throwing — cover art is optional until SteamGridDB is wired in.
///
/// v0.9.6: pedido do usuário ("a aba Todos parece que tá começando a
/// ficar lenta"). Causa: cada Convert() fazia <c>new Bitmap(path)</c> —
/// decodifica o arquivo do disco DE NOVO toda vez, na UI thread. Como
/// RebuildGamesFromLibrary (MainViewModel) cria um GameTileViewModel NOVO
/// pra cada jogo em toda troca de aba/favoritar/atualizar, cada troca
/// redecodificava a capa de TODOS os tiles visíveis do zero — e "Todos"
/// (a lista com mais jogos, principalmente depois das lojas novas em
/// v0.9.3) é exatamente onde isso mais pesa; "Não instalados" é um
/// subconjunto bem menor, por isso parecia fluida por comparação.
///
/// Agora cacheia o Bitmap já decodificado por caminho, junto da data de
/// modificação do arquivo (invalida sozinho se o usuário trocar a capa —
/// ArtworkService sobrescreve o arquivo, o que muda a data). Cresce com o
/// tamanho da biblioteca (sem expiração) — aceitável pro público (uma
/// biblioteca pessoal, não milhares de jogos) e pro RAM de um handheld
/// atual; se um dia isso virar problema de verdade, dá pra trocar por um
/// cache com limite (LRU).
/// </summary>
public sealed class FileToBitmapConverter : IValueConverter
{
    public static readonly FileToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, (DateTime LastWriteUtc, Bitmap Bitmap)> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var lastWriteUtc = File.GetLastWriteTimeUtc(path);

            if (Cache.TryGetValue(path, out var cached) && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Bitmap;
            }

            var bitmap = new Bitmap(path);
            Cache[path] = (lastWriteUtc, bitmap);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
