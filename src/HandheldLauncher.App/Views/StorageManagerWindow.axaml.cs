using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views;

/// <summary>
/// v0.18 "Gerenciador de armazenamento" (Fase 1, 1ª leva — só consulta).
/// Tamanho por jogo é calculado em segundo plano porque somar arquivo por
/// arquivo de uma pasta de jogo grande pode levar alguns segundos; a janela
/// não trava enquanto isso roda.
/// </summary>
public partial class StorageManagerWindow : Window
{
    private readonly MainViewModel? _vm;

    public ObservableCollection<DriveRowViewModel> Drives { get; } = new();
    public ObservableCollection<GameStorageRowViewModel> GameRows { get; } = new();

    public StorageManagerWindow() : this(null) { }

    public StorageManagerWindow(MainViewModel? vm)
    {
        _vm = vm;
        InitializeComponent();
        DriveList.ItemsSource = Drives;
        GameList.ItemsSource = GameRows;

        try { ThemeManager.ApplyFont(this, AppSettingsStore.Load().UiFont); }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }

        RefreshDrives();
        LoadGameSizesAsync();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void RefreshDrives()
    {
        Drives.Clear();
        var lowSpaceWarnings = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;

                var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                if (totalGb < 0.5) continue;

                var freeGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var usedPercent = totalGb > 0 ? (1.0 - freeGb / totalGb) * 100.0 : 0;

                Drives.Add(new DriveRowViewModel
                {
                    Label = $"{drive.Name} ({(drive.DriveType == DriveType.Removable ? "microSD/externo" : "interno")})",
                    UsageText = $"{freeGb:F0} GB livres de {totalGb:F0} GB",
                    UsedPercent = Math.Clamp(usedPercent, 0, 100),
                });

                // v0.18: aviso quando sobra menos de 10% (e menos de 15GB em
                // valor absoluto, pra não disparar num disco de 2TB com 150GB
                // livres — isso não é "pouco espaço" na prática).
                if (freeGb < 15 && usedPercent > 90)
                    lowSpaceWarnings.Add($"{drive.Name} está com só {freeGb:F0} GB livres.");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[ARMAZENAMENTO] Falha ao listar discos: {ex.Message}");
        }

        if (lowSpaceWarnings.Count > 0)
        {
            TxtLowSpaceWarning.Text = "⚠ Pouco espaço: " + string.Join(" ", lowSpaceWarnings);
            LowSpaceWarning.IsVisible = true;
        }
    }

    private void LoadGameSizesAsync()
    {
        var games = _vm?.Games.Select(t => t.Game).ToList() ?? new List<GameEntry>();

        var withDir = games
            .Where(g => !string.IsNullOrWhiteSpace(g.InstallDirectory) && Directory.Exists(g.InstallDirectory))
            .ToList();

        if (withDir.Count == 0)
        {
            TxtNoGames.IsVisible = true;
            return;
        }

        var rows = withDir
            .Select(g => new GameStorageRowViewModel { Title = g.Title, InstallDirectory = g.InstallDirectory, SourceLabel = g.Source.ToString() })
            .ToList();
        foreach (var row in rows) GameRows.Add(row);

        _ = Task.Run(() =>
        {
            foreach (var row in rows)
            {
                long bytes;
                try { bytes = GetDirectorySizeBytes(row.InstallDirectory!); }
                catch (Exception ex)
                {
                    DebugLog.Write($"[ARMAZENAMENTO] Falha ao medir '{row.Title}': {ex.Message}");
                    bytes = 0;
                }

                row.SizeBytes = bytes;
                var text = FormatSize(bytes);
                Dispatcher.UIThread.Post(() => row.SizeText = text);
            }

            Dispatcher.UIThread.Post(() =>
            {
                var ordered = GameRows.OrderByDescending(r => r.SizeBytes).ToList();
                GameRows.Clear();
                foreach (var row in ordered) GameRows.Add(row);

                var totalGb = ordered.Sum(r => r.SizeBytes) / 1024.0 / 1024.0 / 1024.0;
                TxtTotalGamesSize.Text = $"{totalGb:F1} GB no total";
            });
        });
    }

    /// <summary>
    /// Soma recursiva tolerante a erro: uma subpasta sem permissão (comum em
    /// pastas de save/log de alguns jogos) não derruba a conta inteira — só
    /// fica de fora, e o resultado final é "pelo menos isso", nunca uma
    /// exceção que interrompe a leitura dos outros jogos.
    /// </summary>
    private static long GetDirectorySizeBytes(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try { total += new FileInfo(file).Length; }
                catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
            }
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                total += GetDirectorySizeBytes(dir);
            }
        }
        catch (Exception swallowedEx) { DebugLog.Swallowed(swallowedEx); }
        return total;
    }

    private static string FormatSize(long bytes)
    {
        var gb = bytes / 1024.0 / 1024.0 / 1024.0;
        if (gb >= 1.0) return $"{gb:F1} GB";
        var mb = bytes / 1024.0 / 1024.0;
        return $"{mb:F0} MB";
    }
}
