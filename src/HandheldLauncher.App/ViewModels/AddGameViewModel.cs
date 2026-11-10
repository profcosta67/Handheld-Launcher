using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

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

    private readonly List<InstalledProgramOption> _allPrograms = new();

    /// <summary>v0.25.3: lista já filtrada pela procura (ligada ao ListBox).</summary>
    public ObservableCollection<InstalledProgramOption> FilteredPrograms { get; } = new();

    private string _programSearch = string.Empty;
    public string ProgramSearch
    {
        get => _programSearch;
        set
        {
            if (SetField(ref _programSearch, value)) ApplyProgramFilter(value);
        }
    }

    private bool _isScanningPrograms;
    public bool IsScanningPrograms
    {
        get => _isScanningPrograms;
        private set
        {
            if (!SetField(ref _isScanningPrograms, value)) return;
            OnPropertyChanged(nameof(ProgramStatusText));
            OnPropertyChanged(nameof(HasPrograms));
            OnPropertyChanged(nameof(NoProgramsFound));
        }
    }

    private InstalledProgramOption? _selectedProgram;
    public InstalledProgramOption? SelectedProgram
    {
        get => _selectedProgram;
        set
        {
            if (SetField(ref _selectedProgram, value)) UseProgram(value);
        }
    }

    public string ProgramStatusText => IsScanningPrograms
        ? "A procurar programas instalados..."
        : _allPrograms.Count == 0
            ? "Nenhum programa encontrado."
            : $"{FilteredPrograms.Count} de {_allPrograms.Count} programas";

    public bool HasPrograms => FilteredPrograms.Count > 0;
    public bool NoProgramsFound => !IsScanningPrograms && _allPrograms.Count > 0 && FilteredPrograms.Count == 0;

    /// <summary>Lê o registo/App Paths/Menu Iniciar fora da UI thread.</summary>
    public async Task LoadInstalledProgramsAsync()
    {
        if (IsScanningPrograms) return;
        IsScanningPrograms = true;
        try
        {
            var scanned = await Task.Run(() => new InstalledProgramsScanner().Scan()).ConfigureAwait(true);
            _allPrograms.Clear();
            _allPrograms.AddRange(scanned);
            ApplyProgramFilter(ProgramSearch);
        }
        catch
        {
            _allPrograms.Clear();
            FilteredPrograms.Clear();
        }
        finally
        {
            IsScanningPrograms = false;
            OnPropertyChanged(nameof(ProgramStatusText));
            OnPropertyChanged(nameof(HasPrograms));
            OnPropertyChanged(nameof(NoProgramsFound));
        }
    }

    /// <summary>Filtra por nome ou caminho, ignorando acentos e maiúsculas.</summary>
    public void ApplyProgramFilter(string? query)
    {
        var needle = Normalize(query ?? string.Empty);
        var filtered = needle.Length == 0
            ? _allPrograms
            : _allPrograms.Where(p =>
                Normalize(p.DisplayName).Contains(needle, StringComparison.Ordinal) ||
                Normalize(p.ExecutablePath).Contains(needle, StringComparison.Ordinal)).ToList();

        FilteredPrograms.Clear();
        foreach (var program in filtered) FilteredPrograms.Add(program);

        OnPropertyChanged(nameof(ProgramStatusText));
        OnPropertyChanged(nameof(HasPrograms));
        OnPropertyChanged(nameof(NoProgramsFound));
    }

    /// <summary>Preenche os campos com a entrada escolhida; os campos continuam editáveis.</summary>
    public void UseProgram(InstalledProgramOption? program)
    {
        if (program is null) return;
        Title = program.DisplayName;
        ExecutablePath = program.ExecutablePath;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(character));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
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
