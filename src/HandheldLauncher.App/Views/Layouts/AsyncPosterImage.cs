using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Views.Layouts;

/// <summary>
/// v0.18.3 (pente fino — revisão): capa da grade da aba Nuvem.
///
/// Antes a grade usava o FileToBitmapConverter da biblioteca: decodifica na
/// thread da tela e guarda TODO bitmap pra sempre num cache estático. Com os
/// ~560 jogos da nuvem (a grade não virtualiza — todos os blocos existem ao
/// mesmo tempo) isso dava alguns segundos de tela travada na 1ª abertura e
/// ~300 MB de RAM presos até fechar o launcher.
///
/// Aqui: decodifica em segundo plano (a tela não trava; as capas vão
/// aparecendo), já no tamanho do bloco (<see cref="DecodeWidth"/>, menos
/// memória) e sem cache global — quando a aba fecha, os blocos somem e a
/// memória volta. Só a grade da Nuvem usa isto; a biblioteca normal não mudou.
/// </summary>
public class AsyncPosterImage : Image
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<AsyncPosterImage, string?>(nameof(Path));

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>Largura de decodificação em pixels (bloco de 150 a até 150% de escala).</summary>
    public int DecodeWidth { get; set; } = 225;

    private int _version;

    protected override Type StyleKeyOverride => typeof(Image);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PathProperty) _ = LoadAsync(change.GetNewValue<string?>());
    }

    private async Task LoadAsync(string? path)
    {
        var version = ++_version;
        Source = null;
        if (string.IsNullOrWhiteSpace(path)) return;

        Bitmap? bitmap;
        try
        {
            var width = DecodeWidth;
            bitmap = await Task.Run(() =>
            {
                if (!File.Exists(path)) return null;
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality);
            });
        }
        catch (Exception ex)
        {
            DebugLog.Swallowed(ex);
            return;
        }

        if (bitmap is null) return;
        if (version != _version) { bitmap.Dispose(); return; } // o bloco já mudou de jogo enquanto carregava
        Source = bitmap;
    }
}
