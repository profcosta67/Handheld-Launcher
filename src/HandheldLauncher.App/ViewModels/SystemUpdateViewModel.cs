using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.ViewModels;

public class SystemUpdateViewModel : ViewModelBase
{
    private readonly WindowsUpdateService _updateService = new();
    
    public ObservableCollection<SystemUpdateItem> Updates { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    private string _statusMessage = "Pronto para buscar.";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public async Task SearchAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;
        StatusMessage = "Buscando atualizações no Windows Update...";
        Updates.Clear();

        var results = await _updateService.SearchForUpdatesAsync();
        
        foreach (var item in results)
        {
            Updates.Add(item);
        }

        StatusMessage = results.Count == 0 ? "Tudo atualizado!" : $"{results.Count} atualizações encontradas.";
        IsBusy = false;
    }

    public async Task InstallSelectedAsync()
    {
        if (IsBusy || Updates.Count == 0) return;
        
        IsBusy = true;
        var itemsToInstall = new List<SystemUpdateItem>(Updates);
        
        bool success = await _updateService.DownloadAndInstallUpdatesAsync(itemsToInstall, (percent, msg) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Progress = percent;
                StatusMessage = msg;
            });
        });

        if (success)
        {
            StatusMessage = "Atualizações instaladas com sucesso!";
            Updates.Clear();
        }
        else
        {
            StatusMessage = "Ocorreu um erro ou não havia atualizações selecionadas.";
        }
        
        IsBusy = false;
    }
}