using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services.Hud;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.28 (pedido do usuário: tirar a dependência do RTSS): o HUD de desempenho desenhado pelo
/// próprio launcher. Janela transparente no canto de cima, sempre no topo, que não pega foco nem
/// clique — igual ao selo "GRAVANDO". Recebe o mesmo texto que iria para o RTSS (marcas
/// &lt;C=cor&gt;) e mostra com as mesmas cores.
///
/// Limite: jogo em tela cheia EXCLUSIVA desenha por cima de qualquer janela; nesse caso o HUD
/// usa o RTSS, se estiver instalado (ver PerformanceHud).
/// </summary>
public partial class NativeHudWindow : Window
{
    private static readonly IBrush DefaultBrush = Brushes.White;
    private static readonly Dictionary<string, IBrush> BrushCache = new(StringComparer.Ordinal);
    private string? _lastText;

    public NativeHudWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MakeClickThrough();
        PlaceTopLeft();
    }

    /// <summary>Troca o conteúdo (texto com as marcas de cor do RTSS). Só redesenha se mudou.</summary>
    public void SetText(string text)
    {
        if (text == _lastText) { KeepOnTop(); return; }
        _lastText = text;
        Lines.Children.Clear();
        foreach (var line in HudMarkup.Parse(text))
        {
            var block = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = DefaultBrush,
            };
            foreach (var run in line)
                block.Inlines!.Add(new Run(run.Text) { Foreground = BrushFor(run.Color) });
            Lines.Children.Add(block);
        }
        KeepOnTop();
    }

    private static IBrush BrushFor(string? hex)
    {
        if (hex is null) return DefaultBrush;
        if (BrushCache.TryGetValue(hex, out var b)) return b;
        b = Color.TryParse("#" + hex, out var c) ? new ImmutableSolidColorBrush(c) : DefaultBrush;
        BrushCache[hex] = b;
        return b;
    }

    private void PlaceTopLeft()
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
            // tela inteira (não só a área de trabalho): no jogo a barra de tarefas não aparece
            Position = new PixelPoint(screen.Bounds.X + (int)(10 * scale), screen.Bounds.Y + (int)(8 * scale));
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    /// <summary>Alguns jogos se põem "sempre no topo" também; reafirma sem tirar o foco deles.</summary>
    private void KeepOnTop()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private void MakeClickThrough()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow | WsExTransparent);
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
