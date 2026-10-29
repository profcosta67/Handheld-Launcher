using System.Text.Json;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public interface IEpicLibraryScanner
{
    IReadOnlyList<GameEntry> ScanInstalledGames();
}

/// <summary>
/// Integração Epic, etapa 1 (v0.9.1): jogos da Epic Games Store
/// INSTALADOS neste PC, lidos dos manifestos que o próprio aplicativo da
/// Epic mantém em %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item
/// (um JSON por jogo instalado).
///
/// Sem login, de propósito: a Epic não oferece API pública pra launchers de
/// terceiros, e os caminhos não oficiais (reusar o login do aplicativo da
/// Epic) ficam numa zona cinza dos termos de uso. Por isso não existe lista
/// de "não instalados" da Epic. O jogo abre pelo link oficial
/// com.epicgames.launcher:// — o próprio aplicativo da Epic cuida da conta,
/// do DRM e das atualizações; abrir o .exe direto costuma falhar nos jogos
/// da Epic justamente por falta dessa autenticação.
/// </summary>
public sealed class EpicLibraryScanner : IEpicLibraryScanner
{
    private readonly string _manifestsDir;

    public EpicLibraryScanner(string? manifestsDirOverride = null)
    {
        _manifestsDir = manifestsDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var games = new List<GameEntry>();
        if (!Directory.Exists(_manifestsDir)) return games;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_manifestsDir, "*.item");
        }
        catch
        {
            return games;
        }

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                if (GetBool(root, "bIsIncompleteInstall")) continue;

                var appName = GetString(root, "AppName");
                var title = GetString(root, "DisplayName");
                var installLocation = GetString(root, "InstallLocation");
                var catalogNamespace = GetString(root, "CatalogNamespace");
                var catalogItemId = GetString(root, "CatalogItemId");
                if (appName is null || title is null || installLocation is null) continue;
                if (!Directory.Exists(installLocation)) continue;

                // DLC/complementos têm o jogo principal em MainGameAppName.
                var mainGame = GetString(root, "MainGameAppName");
                if (mainGame is not null && !string.Equals(mainGame, appName, StringComparison.OrdinalIgnoreCase)) continue;

                // Quando a lista de categorias existe, só "games" (não addons/plugins).
                if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
                    && categories.GetArrayLength() > 0
                    && !categories.EnumerateArray().Any(c => string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var launchExecutable = GetString(root, "LaunchExecutable");
                var executablePath = launchExecutable is null ? null : Path.Combine(installLocation, launchExecutable);

                games.Add(new GameEntry
                {
                    Id = $"epic_{appName}",
                    Title = title,
                    Source = GameSource.EpicGames,
                    InstallDirectory = installLocation,
                    ExecutablePath = executablePath,
                    LaunchUri = catalogNamespace is not null && catalogItemId is not null
                        ? $"com.epicgames.launcher://apps/{Uri.EscapeDataString(catalogNamespace)}%3A{Uri.EscapeDataString(catalogItemId)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true"
                        : $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true",
                });
            }
            catch
            {
                // Manifesto corrompido/de formato inesperado: pula só esse jogo.
            }
        }

        return games;
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
