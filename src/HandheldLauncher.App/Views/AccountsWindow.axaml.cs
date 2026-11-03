using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Input;

namespace HandheldLauncher.App.Views;

/// <summary>
/// Diálogo "Gerenciar contas e chaves de API", aberto a partir de
/// SettingsWindow (botão 🔒). Ver comentário no .axaml pra decisão de
/// design (mesma ViewModel do caller, sem Save próprio, campos mascarados
/// por padrão).
/// </summary>
public partial class AccountsWindow : Window
{
    // Só pro designer/casos sem parâmetro — SettingsWindow sempre usa o
    // outro construtor, passando a MESMA ViewModel (não uma cópia).
    public AccountsWindow() : this(new SettingsViewModel())
    {
    }

    public AccountsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    private const uint KeyUp = 0x0002;

    /// <summary>
    /// Enquanto uma página de chave estiver aberta no navegador externo, o
    /// gamepad precisa enviar teclas ao navegador, não à janela modal atrás dele.
    /// X funciona como Tab para percorrer links e campos; A ativa o item.
    /// </summary>
    public void HandleGamepadAction(GamepadAction action)
    {
        if (IsBrowserForeground())
        {
            switch (action)
            {
                case GamepadAction.MoveUp: SendKey(0x26); break;
                case GamepadAction.MoveDown: SendKey(0x28); break;
                case GamepadAction.MoveLeft: SendKey(0x25); break;
                case GamepadAction.MoveRight: SendKey(0x27); break;
                case GamepadAction.Confirm: SendKey(0x0D); break;
                case GamepadAction.Back: SendKey(0x1B); break;
                case GamepadAction.GameDetails: SendKey(0x09); break; // X = Tab
                case GamepadAction.PreviousFilter: SendKey(0xA0); SendKey(0x09); break; // Shift+Tab
                case GamepadAction.NextFilter: SendKey(0x09); break;
            }
            return;
        }

        DialogGamepadNavigator.Handle(this, action);
    }

    private bool IsBrowserForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return name.Equals("msedge", StringComparison.OrdinalIgnoreCase)
                || name.Equals("chrome", StringComparison.OrdinalIgnoreCase)
                || name.Equals("firefox", StringComparison.OrdinalIgnoreCase)
                || name.Equals("brave", StringComparison.OrdinalIgnoreCase)
                || name.Equals("opera", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void SendKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyUp, UIntPtr.Zero);
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    private void OnUseSteamAccountClick(object? sender, RoutedEventArgs e) => ViewModel.UseDetectedSteamAccount();

    private void OnGetSteamKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://steamcommunity.com/dev/apikey");

    private void OnGetGridDbKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://www.steamgriddb.com/profile/preferences/api");

    // v0.14: botão "Obter chave" do bloco RAWG — aponta pra doc oficial.
    private void OnGetRawgKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://rawg.io/apidocs");

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Melhor esforço.
        }
    }

    // "Proteção de visualização" pedida pelo usuário: mascarado (•) por
    // padrão, 👁 alterna pra texto puro — só estado de UI, por isso vive
    // aqui e não na ViewModel (zera sempre que a tela reabre, de propósito).
    private static void ToggleMask(TextBox box) => box.PasswordChar = box.PasswordChar == default ? '•' : default;

    private void OnToggleGridDbVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(GridDbKeyBox);

    private void OnToggleSteamWebVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(SteamWebKeyBox);

    private void OnToggleSteamIdVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(SteamIdBox);

    // v0.14: máscara do campo RAWG.
    private void OnToggleRawgVisibilityClick(object? sender, RoutedEventArgs e) => ToggleMask(RawgKeyBox);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}