namespace HandheldLauncher.Core.Services;

/// <summary>Conta Steam encontrada no próprio PC (sem login, sem senha).</summary>
public sealed record SteamLocalAccount(string SteamId64, string PersonaName);

/// <summary>
/// Integração com a Steam, etapa 1 (v0.9.0): descobre QUAL conta Steam está
/// logada neste aparelho lendo o arquivo que o próprio cliente Steam mantém
/// (config\loginusers.vdf) — a conta marcada como "MostRecent". Com isso o
/// SteamID64 deixa de ser digitado à mão.
///
/// Segurança, de propósito: o launcher NUNCA pede, lê nem guarda senha ou
/// token de login da Steam. Desse arquivo só usa o SteamID64 (público por
/// natureza, é o número do perfil) e o apelido (PersonaName) pra mostrar na
/// tela. O nome de usuário de login (AccountName) é ignorado.
/// </summary>
public static class SteamAccountDetector
{
    public static SteamLocalAccount? FindMostRecent(string? steamRoot = null)
    {
        try
        {
            steamRoot ??= SteamLibraryScanner.LocateSteamRoot();
            if (steamRoot is null) return null;

            var path = Path.Combine(steamRoot, "config", "loginusers.vdf");
            if (!File.Exists(path)) return null;

            var root = VdfParser.ParseFile(path);
            var users = root["users"] ?? root;

            SteamLocalAccount? fallback = null;
            foreach (var (steamId, user) in users.Children)
            {
                if (steamId.Length != 17 || !steamId.All(char.IsDigit)) continue;

                var account = new SteamLocalAccount(steamId, user.GetString("PersonaName") ?? "Conta Steam");
                if (user.GetString("MostRecent") == "1") return account;
                fallback ??= account;
            }

            return fallback;
        }
        catch
        {
            return null;
        }
    }
}
