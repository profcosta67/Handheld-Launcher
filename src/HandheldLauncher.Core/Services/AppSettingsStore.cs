using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static void LogError(string message, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[AppSettingsStore] {message} Detalhes: {ex.Message}");
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "settings.json");

    public static AppSettings Load(string? pathOverride = null)
    {
        var path = pathOverride ?? DefaultPath;

        if (!File.Exists(path))
        {
            var fresh = new AppSettings();
            Save(fresh, path);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            var hadPlainSecret = IsPlain(settings.SteamGridDbApiKey) || IsPlain(settings.SteamWebApiKey);

            settings.SteamGridDbApiKey = Unprotect(settings.SteamGridDbApiKey);
            settings.SteamWebApiKey = Unprotect(settings.SteamWebApiKey);

            if (hadPlainSecret && OperatingSystem.IsWindows())
            {
                try { Save(settings, path); } catch { /* melhor esforço */ }
            }

            return settings;
        }
        catch (Exception ex)
        {
            LogError("Falha ao carregar settings.json. Usando configurações padrão.", ex);
            return new AppSettings();
        }
    }

    // ------------------------------------------------------------------
    // Proteção das chaves: 
    // Windows = DPAPI (seguro).
    // Linux = Texto puro com permissão de arquivo restrita (0600).
    // ------------------------------------------------------------------

    private const string ProtectedPrefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HandheldLauncher.settings.v1");

    private static bool IsPlain(string? value)
        => !string.IsNullOrEmpty(value) && !value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);

    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        if (!OperatingSystem.IsWindows())
        {
            // No Linux não temos DPAPI. O arquivo será protegido via permissão 0600 (ver Save).
            return value; 
        }

        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(bytes);
        }
        catch
        {
            return value;
        }
    }

    private static string? Unprotect(string? value)
    {
        if (value is null || !value.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) return value;

        if (!OperatingSystem.IsWindows())
        {
            // Arquivo veio do Windows, não conseguimos ler as chaves aqui.
            // Retorna null para o usuário digitar de novo.
            return null; 
        }

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[ProtectedPrefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppSettings settings, string? pathOverride = null)
    {
        var path = pathOverride ?? DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var node = JsonSerializer.SerializeToNode(settings, JsonOptions) as JsonObject;
        if (node is null)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
            return;
        }

        node[nameof(AppSettings.SteamGridDbApiKey)] = Protect(settings.SteamGridDbApiKey);
        node[nameof(AppSettings.SteamWebApiKey)] = Protect(settings.SteamWebApiKey);
        
        File.WriteAllText(path, node.ToJsonString(JsonOptions));

        // NOVO: No Linux, restringe a permissão do arquivo para 0600 (apenas o dono lê/escreve).
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // Chmod 0600 (UserRead | UserWrite)
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                LogError("Não foi possível alterar as permissões do settings.json no Linux.", ex);
            }
        }
    }
}