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
    /// 18/11: "Adicionar jogo" e "Adicionar programa" viraram telas separadas (antes era uma
    /// tela só com a caixa "Adicionar como Programa"). Definido por quem abre a janela.
    /// </summary>
    private bool _isApplication;
    public bool IsApplication
    {
        get => _isApplication;
        set
        {
            if (!SetField(ref _isApplication, value)) return;
            OnPropertyChanged(nameof(IsGameMode));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(NameHeader));
            OnPropertyChanged(nameof(NameWatermark));
            OnPropertyChanged(nameof(SaveLabel));
        }
    }

    public bool IsGameMode => !IsApplication;
    public string WindowTitle => IsApplication ? "Adicionar programa" : "Adicionar jogo";
    public string NameHeader => IsApplication ? "Nome do programa" : "Nome do jogo";
    public string NameWatermark => IsApplication ? "Ex: Discord" : "Ex: Hollow Knight";
    public string SaveLabel => IsApplication ? "Salvar programa" : "Salvar jogo";

    /// <summary>
    /// 18/11: jogo de emulador — o arquivo do jogo (.nsp, .xci, .iso...) vai entre aspas no fim
    /// dos argumentos. Ex.: executável Ryujinx.exe + arquivo do Pokémon.
    /// </summary>
    private string _gameFilePath = string.Empty;
    public string GameFilePath
    {
        get => _gameFilePath;
        set => SetField(ref _gameFilePath, value);
    }

    /// <summary>18/11: aviso curto depois de escolher um atalho (o que foi lido dele).</summary>
    private string _shortcutNote = string.Empty;
    public string ShortcutNote
    {
        get => _shortcutNote;
        set
        {
            if (SetField(ref _shortcutNote, value)) OnPropertyChanged(nameof(HasShortcutNote));
        }
    }
    public bool HasShortcutNote => !string.IsNullOrWhiteSpace(ShortcutNote);

    /// <summary>
    /// 18/11: aplica o que foi lido de um atalho escolhido no "Procurar". Atalho com executável
    /// (ex.: o do Ryujinx) vira executável + argumentos + pasta; .url ou atalho sem executável
    /// fica como está e abre pelo Windows.
    /// </summary>
    public void ApplyShortcut(string shortcutPath, string? targetPath, string? arguments, string? workingDirectory, string? url)
    {
        if (string.IsNullOrWhiteSpace(Title))
            Title = Path.GetFileNameWithoutExtension(shortcutPath);

        if (!string.IsNullOrWhiteSpace(url))
        {
            ExecutablePath = url!;
            ShortcutNote = "Atalho de internet: o jogo abre pelo Windows (ex.: pela Steam).";
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetPath)
            && targetPath!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ExecutablePath = targetPath;
            LaunchArguments = arguments ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
            ShortcutNote = $"Atalho lido: abre {Path.GetFileName(targetPath)}"
                + (string.IsNullOrWhiteSpace(arguments) ? "." : " com os argumentos do atalho.");
            return;
        }

        ExecutablePath = shortcutPath;
        ShortcutNote = "Atalho sem executável direto: o jogo abre pelo Windows, e o launcher pode não perceber quando ele fechar.";
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

    /// <summary>
    /// Builds the GameEntry to hand to ILibraryService.AddManualGame. Only call when CanSave is true.
    /// 18/11: regras em <see cref="ManualLaunchTarget"/> (atalho/endereço abre pelo Windows;
    /// emulador de Switch cai no filtro "Emuladores"; arquivo do jogo vai no fim dos argumentos).
    /// </summary>
    public GameEntry BuildGameEntry() => ManualLaunchTarget.BuildEntry(
        Title, ExecutablePath, LaunchArguments, WorkingDirectory,
        IsApplication ? null : GameFilePath,
        IsApplication, !IsApplication && IsXboxSource);
}
