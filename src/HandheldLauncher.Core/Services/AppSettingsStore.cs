using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandheldLauncher", "settings.json");

    /// <summary>
    /// Loads settings.json, creating it with empty fields if it doesn't
    /// exist yet — read/written through the "Configurações" dialog
    /// (SettingsWindow), hand-editing the file still works too.
    /// </summary>
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

            // Chaves gravadas em texto puro (versões antigas, ou coladas à
            // mão no arquivo) são criptografadas na primeira leitura.
            var hadPlainSecret = IsPlain(settings.SteamGridDbApiKey) || IsPlain(settings.SteamWebApiKey);

            settings.SteamGridDbApiKey = Unprotect(settings.SteamGridDbApiKey);
            settings.SteamWebApiKey = Unprotect(settings.SteamWebApiKey);

            if (hadPlainSecret && OperatingSystem.IsWindows())
            {
                try { Save(settings, path); } catch { /* melhor esforço */ }
            }

            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    // ------------------------------------------------------------------
    // Proteção das chaves (v0.9.0): a chave do SteamGridDB e a da Steam Web
    // API ficam no settings.json criptografadas com o DPAPI do Windows
    // (escopo do usuário atual) — copiar o arquivo pra outro PC ou outro
    // usuário não revela as chaves. Em memória o resto do app continua
    // vendo o texto normal. Valor criptografado = "dpapi:" + base64.
    // ------------------------------------------------------------------

    private const string ProtectedPrefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HandheldLauncher.settings.v1");

    private static bool IsPlain(string? value)
        => !string.IsNullOrEmpty(value) && !value.StartsWith(ProtectedPrefix, StringComparison.Ordinal);

    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows()) return value;
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
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[ProtectedPrefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Arquivo veio de outro PC/usuário: a chave não pode ser lida aqui.
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
    }
}
