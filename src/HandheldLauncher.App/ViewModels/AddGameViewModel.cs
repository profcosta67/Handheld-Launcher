using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Backs the "Adicionar jogo" dialog (AddGameWindow). Deliberately plain —
/// four text fields and a CanSave gate — since the dialog's own code-behind
/// handles the file-picker and Close(dialogResult) plumbing, which belong
/// to the Window, not here.
/// </summary>
public sealed class AddGameViewModel : ViewModelBase
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value)) OnPropertyChanged(nameof(CanSave));
        }
    }

    private string _executablePath = string.Empty;
    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (SetField(ref _executablePath, value)) OnPropertyChanged(nameof(CanSave));
        }
    }

    private string _launchArguments = string.Empty;
    public string LaunchArguments
    {
        get => _launchArguments;
        set => SetField(ref _launchArguments, value);
    }

    private string _workingDirectory = string.Empty;
    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetField(ref _workingDirectory, value);
    }

    /// <summary>
    /// Marca a entrada manual como GameSource.XboxApp em vez de Manual —
    /// não existe scanner do Xbox app ainda, então esse checkbox é a única
    /// forma de um jogo cair na aba de filtro "Xbox" pedida pelo usuário.
    /// </summary>
    private bool _isApplication;
    public bool IsApplication
    {
        get => _isApplication;
        set => SetField(ref _isApplication, value);
    }

    private bool _isXboxSource;
    public bool IsXboxSource
    {
        get => _isXboxSource;
        set => SetField(ref _isXboxSource, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(ExecutablePath);

    /// <summary>Builds the GameEntry to hand to ILibraryService.AddManualGame. Only call when CanSave is true.</summary>
    public GameEntry BuildGameEntry() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = Title.Trim(),
        IsApplication = IsApplication,
        Source = IsXboxSource ? GameSource.XboxApp : GameSource.Manual,
        ExecutablePath = ExecutablePath.Trim(),
        LaunchArguments = string.IsNullOrWhiteSpace(LaunchArguments) ? null : LaunchArguments.Trim(),
        WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim(),
    };
}
