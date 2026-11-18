using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Views;

/// <summary>
/// 18/11 (pedido do usuário): aviso de gravação por cima de tudo — contagem "3, 2, 1" antes de
/// começar e o selo "GRAVANDO" enquanto durar. Vale na tela inicial do launcher e com jogo aberto:
/// é uma janela própria, sempre no topo, que não pega foco nem clique (o aperto passa direto para
/// quem está embaixo), então não atrapalha o jogo nem a navegação.
///
/// Antes, quem gravava pela tela inicial não tinha nenhum sinal na tela (o "[ REC ]" só aparecia
/// no HUD do RTSS, e só com jogo aberto).
/// </summary>
public partial class RecordingOverlayWindow : Window
{
    private readonly DispatcherTimer _blinkTimer;
    private bool _dotVisible = true;

    public RecordingOverlayWindow()
    {
        InitializeComponent();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _blinkTimer.Tick += (_, _) =>
        {
            _dotVisible = !_dotVisible;
            BadgeDot.Opacity = _dotVisible ? 1 : 0.15;
        };
        Closed += (_, _) => _blinkTimer.Stop();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MakeClickThrough();
        Place(top: false);
    }

    /// <summary>Mostra um número da contagem (3, 2, 1).</summary>
    public void ShowCountdown(int number)
    {
        TxtCountdown.Text = number.ToString();
        CountdownBox.IsVisible = true;
        Place(top: false);     // contagem no meio da tela
    }

    public void HideCountdown() => CountdownBox.IsVisible = false;

    /// <summary>Liga o selo "GRAVANDO" (com o ponto piscando).</summary>
    public void ShowBadge()
    {
        BadgeBox.IsVisible = true;
        Place(top: true);      // selo no alto, longe do meio da tela
        if (!_blinkTimer.IsEnabled) _blinkTimer.Start();
    }

    public void HideBadge()
    {
        BadgeBox.IsVisible = false;
        _blinkTimer.Stop();
        BadgeDot.Opacity = 1;
    }

    /// <summary>Tempo mostrado ao lado do selo (mm:ss).</summary>
    public void SetElapsed(TimeSpan elapsed) =>
        TxtBadge.Text = $"GRAVANDO  {elapsed.Minutes:00}:{elapsed.Seconds:00}";

    public bool HasAnythingVisible => CountdownBox.IsVisible || BadgeBox.IsVisible;

    /// <summary>Põe a janela no meio da tela (contagem) ou encostada no alto (selo).</summary>
    private void Place(bool top)
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var area = screen.WorkingArea;
            var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
            var width = (int)(Width * scale);
            var height = (int)(Height * scale);
            Position = new Avalonia.PixelPoint(
                area.X + (area.Width - width) / 2,
                top ? area.Y + (int)(12 * scale) : area.Y + (area.Height - height) / 2);
        }
        catch (Exception ex) { DebugLog.Swallowed(ex); }
    }

    /// <summary>Janela decorativa: não ativa, não aparece no Alt+Tab e deixa o clique passar.</summary>
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
