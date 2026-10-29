using System.Runtime.Versioning;
using HandheldLauncher.Core.Models;
using Microsoft.Win32;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// v0.9.3 — outras lojas, mesmo esquema da Epic: só jogos INSTALADOS, lidos
/// do que a própria loja registra no Windows, sem login e sem senha. Tudo
/// melhor esforço: chave de registro ausente/diferente = lista vazia, nunca
/// erro. Nunca testado contra instalações reais por quem escreveu.
/// </summary>
internal static class RegistryHelper
{
    /// <summary>Abre a chave nas duas visões do registro (32 e 64 bits) — as lojas gravam em lugares diferentes.</summary>
    [SupportedOSPlatform("windows")]
    public static IEnumerable<RegistryKey> OpenBoth(string subKey)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            RegistryKey? key = null;
            try
            {
                key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(subKey);
            }
            catch
            {
                // sem acesso / não existe
            }
            if (key is not null) yield return key;
        }
    }

    public static string? Str(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// GOG (GOG Galaxy ou instalador offline): HKLM\SOFTWARE\GOG.com\Games\&lt;id&gt;
/// com gameName / exe / path / workingDir / launchParam. Jogos da GOG não
/// têm DRM, então abrem pelo .exe direto (e o launcher acompanha o processo,
/// igual um jogo adicionado à mão).
/// </summary>
public sealed class GogLibraryScanner : IStoreLibraryScanner
{
    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var games = new List<GameEntry>();
        if (!OperatingSystem.IsWindows()) return games;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in RegistryHelper.OpenBoth(@"SOFTWARE\GOG.com\Games"))
        {
            using (root)
            {
                foreach (var id in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(id);
                        if (key is null || !seen.Add(id)) continue;

                        var title = RegistryHelper.Str(key, "gameName");
                        var exe = RegistryHelper.Str(key, "exe");
                        var path = RegistryHelper.Str(key, "path");
                        if (title is null || exe is null || !File.Exists(exe)) continue;

                        // Entradas de DLC apontam pro jogo principal em "dependsOn".
                        if (RegistryHelper.Str(key, "dependsOn") is not null) continue;

                        games.Add(new GameEntry
                        {
                            Id = $"gog_{id}",
                            Title = title,
                            Source = GameSource.Gog,
                            ExecutablePath = exe,
                            LaunchArguments = RegistryHelper.Str(key, "launchParam"),
                            WorkingDirectory = RegistryHelper.Str(key, "workingDir") ?? path,
                            InstallDirectory = path,
                        });
                    }
                    catch
                    {
                        // pula só esse jogo
                    }
                }
            }
        }

        return games;
    }
}

/// <summary>
/// Ubisoft Connect: HKLM\SOFTWARE\Ubisoft\Launcher\Installs\&lt;id&gt; com
/// InstallDir. O registro não guarda o nome do jogo, então o título é o nome
/// da pasta de instalação (normalmente o nome do jogo). Abre pelo link
/// oficial uplay://launch/&lt;id&gt;/0 — o Ubisoft Connect cuida da conta.
/// </summary>
public sealed class UbisoftLibraryScanner : IStoreLibraryScanner
{
    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var games = new List<GameEntry>();
        if (!OperatingSystem.IsWindows()) return games;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in RegistryHelper.OpenBoth(@"SOFTWARE\Ubisoft\Launcher\Installs"))
        {
            using (root)
            {
                foreach (var id in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(id);
                        if (key is null || !seen.Add(id)) continue;

                        var dir = RegistryHelper.Str(key, "InstallDir");
                        if (dir is null) continue;
                        dir = dir.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                        if (!Directory.Exists(dir)) continue;

                        var title = Path.GetFileName(dir);
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        games.Add(new GameEntry
                        {
                            Id = $"ubisoft_{id}",
                            Title = title,
                            Source = GameSource.Ubisoft,
                            InstallDirectory = dir,
                            LaunchUri = $"uplay://launch/{id}/0",
                        });
                    }
                    catch
                    {
                        // pula só esse jogo
                    }
                }
            }
        }

        return games;
    }
}

/// <summary>
/// EA app (antigo Origin): o EA app não mantém um índice simples e aberto
/// dos jogos instalados, então aqui vai o caminho mais conservador — as
/// entradas de "Programas instalados" do Windows (Uninstall) publicadas pela
/// Electronic Arts, com pasta de instalação e um .exe de ícone (DisplayIcon)
/// que existe. O jogo abre por esse .exe; o próprio jogo chama o EA app pra
/// autenticar. EXPERIMENTAL: alguns jogos podem não aparecer ou não abrir
/// por aqui — nesses casos, "Adicionar jogo" à mão continua valendo.
/// </summary>
public sealed class EaLibraryScanner : IStoreLibraryScanner
{
    private static readonly string[] SkipNames = ["EA app", "Origin", "EA Desktop", "Electronic Arts", "EA Anticheat", "Redistributable"];

    public IReadOnlyList<GameEntry> ScanInstalledGames()
    {
        var games = new List<GameEntry>();
        if (!OperatingSystem.IsWindows()) return games;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in RegistryHelper.OpenBoth(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            using (root)
            {
                foreach (var id in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(id);
                        if (key is null) continue;

                        var publisher = RegistryHelper.Str(key, "Publisher");
                        if (publisher is null || !publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)) continue;

                        var title = RegistryHelper.Str(key, "DisplayName");
                        var location = RegistryHelper.Str(key, "InstallLocation");
                        if (title is null || location is null || !Directory.Exists(location)) continue;
                        if (SkipNames.Any(n => title.Equals(n, StringComparison.OrdinalIgnoreCase) || title.Contains("Redistributable", StringComparison.OrdinalIgnoreCase))) continue;

                        var exe = ExeFromDisplayIcon(RegistryHelper.Str(key, "DisplayIcon"));
                        if (exe is null || !File.Exists(exe)) continue;
                        if (Path.GetFileName(exe).Contains("unins", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(exe)) continue;

                        games.Add(new GameEntry
                        {
                            Id = $"ea_{id}",
                            Title = title,
                            Source = GameSource.EaApp,
                            ExecutablePath = exe,
                            WorkingDirectory = Path.GetDirectoryName(exe),
                            InstallDirectory = location,
                        });
                    }
                    catch
                    {
                        // pula só essa entrada
                    }
                }
            }
        }

        return games;
    }

    /// <summary>DisplayIcon vem como "C:\...\jogo.exe,0" ou com aspas — tira as duas coisas.</summary>
    private static string? ExeFromDisplayIcon(string? displayIcon)
    {
        if (displayIcon is null) return null;
        var value = displayIcon.Trim().Trim('"');
        var comma = value.LastIndexOf(',');
        if (comma > 0 && !value[(comma + 1)..].Contains('\\')) value = value[..comma];
        value = value.Trim().Trim('"');
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value : null;
    }
}
