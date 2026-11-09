using Avalonia.Controls;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Janela que hospeda o teclado na tela (v0.24.2) e sabe encaminhar-lhe o
/// controle. O MainWindow chama isto ANTES do navegador genérico
/// (<see cref="DialogGamepadNavigator"/>): com o teclado aberto, o D-Pad anda
/// nas teclas e o B fecha o teclado em vez de fechar a janela inteira.
/// </summary>
internal interface IGamepadKeyboardHost
{
    /// <summary>True quando o teclado tratou a ação (ela não deve seguir).</summary>
    bool HandleKeyboardGamepadAction(GamepadAction action);

    /// <summary>
    /// Abre o teclado da janela no campo indicado (A sobre um TextBox, ou
    /// toque/clique no campo). Devolve false se a janela não tem teclado — o
    /// chamador cai então no teclado do Windows.
    /// </summary>
    bool ShowKeyboardFor(TextBox target);
}
