using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.ViewModels;

/// <summary>
/// Backs the "Configurações" dialog (SettingsWindow) — pedido do usuário
/// depois de se perder tentando achar onde colar a Steam Web API key
/// ("seria possível criar uma tela de configuração pelo próprio Launcher?
/// Acho que ficaria mais fácil"). Deliberadamente burra, igual
/// AddGameViewModel: só campos de texto + CanSave/SavedMessage — toda a
/// leitura/escrita de AppSettingsStore e o Close() do diálogo ficam no
/// code-behind da Window, não aqui.
///
/// Edita os mesmos campos que até agora só existiam como hand-edit do
/// settings.json (SteamGridDbApiKey, SteamWebApiKey, SteamId64,
/// XboxScanFolders). IMPORTANTE: salvar aqui NÃO reconfigura os serviços
/// já construídos nesta execução (LibraryService, SteamOwnedGamesClient,
/// ArtworkService etc. recebem os valores por CÓPIA no App.axaml.cs, uma
/// vez só, na abertura do app) — precisa reiniciar o launcher pra valer.
/// Não tentei fazer hot-reload disso porque ir além (destruir/reconstruir
/// os serviços em runtime) é bem mais arriscado de acertar sem conseguir
/// compilar/testar aqui, pra um ganho pequeno (economizar um reinício).
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private string _steamGridDbApiKey = string.Empty;
    public string SteamGridDbApiKey
    {
        get => _steamGridDbApiKey;
        set => SetField(ref _steamGridDbApiKey, value);
    }

    private string _steamWebApiKey = string.Empty;
    public string SteamWebApiKey
    {
        get => _steamWebApiKey;
        set => SetField(ref _steamWebApiKey, value);
    }

    private string _steamId64 = string.Empty;
    public string SteamId64
    {
        get => _steamId64;
        set => SetField(ref _steamId64, value);
    }

    private string _xboxScanFoldersText = string.Empty;

    /// <summary>Uma pasta por linha na caixa de texto — convertido de/pra AppSettings.XboxScanFolders (List&lt;string&gt;) ao carregar/salvar.</summary>
    public string XboxScanFoldersText
    {
        get => _xboxScanFoldersText;
        set => SetField(ref _xboxScanFoldersText, value);
    }

    private string? _savedMessage;
    public string? SavedMessage
    {
        get => _savedMessage;
        private set
        {
            if (SetField(ref _savedMessage, value))
            {
                OnPropertyChanged(nameof(HasSavedMessage));
            }
        }
    }

    public bool HasSavedMessage => !string.IsNullOrEmpty(SavedMessage);

    /// <summary>Carrega os campos a partir de settings.json (via AppSettingsStore.Load), chamado quando a Window abre.</summary>
    public void LoadFrom(AppSettings settings)
    {
        SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty;
        SteamWebApiKey = settings.SteamWebApiKey ?? string.Empty;
        SteamId64 = settings.SteamId64 ?? string.Empty;
        XboxScanFoldersText = string.Join(Environment.NewLine, settings.XboxScanFolders);
        SavedMessage = null;
    }

    /// <summary>
    /// Botão "Salvar" — grava de volta em AppSettings e persiste via
    /// AppSettingsStore.Save (mesmo arquivo/caminho que o hand-edit sempre
    /// usou, %AppData%\HandheldLauncher\settings.json). Não fecha a tela
    /// sozinho — deixa a mensagem de confirmação visível pro usuário ler
    /// o aviso de "precisa reiniciar" antes de fechar.
    /// </summary>
    public void SaveTo(AppSettings settings)
    {
        settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(SteamGridDbApiKey) ? null : SteamGridDbApiKey.Trim();
        settings.SteamWebApiKey = string.IsNullOrWhiteSpace(SteamWebApiKey) ? null : SteamWebApiKey.Trim();
        settings.SteamId64 = string.IsNullOrWhiteSpace(SteamId64) ? null : SteamId64.Trim();
        settings.XboxScanFolders = XboxScanFoldersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        AppSettingsStore.Save(settings);

        SavedMessage = "Salvo! Reinicie o launcher pra aplicar (Minimizar não basta — precisa fechar de vez e abrir novamente).";
    }
}
