using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;

namespace HandheldLauncher.App.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);

        // Posiciona a janela no lado direito da tela
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            
            // Cálculo corrigido usando X, Y, Width e Height
            var rightEdge = workingArea.X + workingArea.Width;
            var topEdge = workingArea.Y;
            var centerY = topEdge + (workingArea.Height - (int)Height) / 2;

            Position = new PixelPoint(
                rightEdge - (int)Width - 20, // 20px de margem da borda direita
                centerY // Centralizado verticalmente
            );
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}