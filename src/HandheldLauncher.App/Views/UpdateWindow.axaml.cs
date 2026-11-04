using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.App.ViewModels;

namespace HandheldLauncher.App.Views;

public partial class UpdateWindow : Window
{
    private readonly MainViewModel? _viewModel;

    public UpdateWindow()
    {
        InitializeComponent();
    }

    public UpdateWindow(MainViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnLaterClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnOpenGitHubClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/profcosta67/Handheld-Launcher/releases") 
            { 
                UseShellExecute = true 
            });
        }
        catch { }
    }

    private void OnUpdateNowClick(object? sender, RoutedEventArgs e)
    {
        // Chama a lógica de aplicar a atualização no MainViewModel
        _viewModel?.ApplyUpdate(); 
        Close();
    }
}