using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using HandheldLauncher.Core.Models;
using Microsoft.Win32;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// v0.25.3 — descobre programas instalados para o diálogo "Adicionar".
///
/// Três fontes, todas de melhor esforço:
///   • registo de desinstalação (HKLM/HKCU, 32 e 64 bits);
///   • App Paths (aplicações que registam um .exe sem entrada de desinstalação);
///   • atalhos do Menu Iniciar (com resolução do destino via WScript.Shell).
///
/// Só entra o que tiver um .exe que exista mesmo no disco e que não pareça
/// um desinstalador/actualizador. O scan nunca lança para o chamador: uma
/// chave estragada ou um atalho partido é simplesmente ignorado. A lista é
/// devolvida ordenada por nome e sem duplicados (pelo caminho do .exe).
/// </summary>
public sealed class InstalledProgramsScanner
{
    private static readonly string[] NoiseNameFragments =
    [
        "visual c++", "redistributable", "security update", "update for", "hotfix",
        "service pack", "windows sdk", "software development kit", "debugging tools",
        "windows driver", "driver package", "webview2", "vulkan run time", "directx",
        "physx", "bonjour", "chipset", "management engine", "update health",
        "windows assessment", "compatibility appraiser", ".net runtime",
        "asp.net", "microsoft edge update", "google update", "installer"
    ];

    private static readonly string[] NoiseExeFragments =
    [
        "unins", "uninstall", "setup", "update", "updater", "helper", "crash",
        "report", "redist", "vc_redist", "dotnet", "msiexec", "rundll32",
        "cmd.exe", "powershell", "elevate", "install"
    ];

    /// <summary>Devolve os programas encontrados. É seguro chamar fora da UI thread.</summary>
    public IReadOnlyList<InstalledProgramOption> Scan()
    {
        var found = new Dictionary<string, InstalledProgramOption>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return Array.Empty<InstalledProgramOption>();

        try { ScanUninstall(found); } catch { /* melhor esforço */ }
        try { ScanAppPaths(found); } catch { /* melhor esforço */ }
        try { ScanStartMenu(found); } catch { /* melhor esforço */ }

        return found.Values
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    [SupportedOSPlatform("windows")]
    private static void ScanUninstall(Dictionary<string, InstalledProgramOption> found)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? root = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (root is null) continue;

                    foreach (var subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(subName);
                            if (key is null) continue;

                            // Componentes internos/actualizações não são programas que o
                            // utilizador queira lançar.
                            if (Str(key, "SystemComponent") == "1") continue;
                            if (Str(key, "ParentKeyName") is not null) continue;
                            var release = Str(key, "ReleaseType");
                            if (release is not null &&
                                (release.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                                 release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                                 release.Contains("ServicePack", StringComparison.OrdinalIgnoreCase)))
                                continue;

                            var displayName = Str(key, "DisplayName");
                            if (IsNoiseName(displayName)) continue;

                            var location = Str(key, "InstallLocation");
                            var exe = ExeFromDisplayIcon(Str(key, "DisplayIcon"));
                            if (exe is null && location is not null)
                                exe = FindExecutableIn(location, displayName);

                            Add(found, displayName, exe, "Registo");
                        }
                        catch
                        {
                            // uma entrada estragada não invalida as restantes
                        }
                    }
                }
                catch
                {
                    // sem acesso a esta vista/hive
                }
                finally
                {
                    root?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ScanAppPaths(Dictionary<string, InstalledProgramOption> found)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? root = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                    if (root is null) continue;

                    foreach (var subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(subName);
                            if (key is null) continue;
                            var exe = key.GetValue(null) as string ?? Str(key, "Path");
                            var displayName = Path.GetFileNameWithoutExtension(subName);
                            Add(found, displayName, exe, "App Paths");
                        }
                        catch
                        {
                            // ignora só esta entrada
                        }
                    }
                }
                catch
                {
                    // sem acesso
                }
                finally
                {
                    root?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ScanStartMenu(Dictionary<string, InstalledProgramOption> found)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;

            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            };

            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

                foreach (var shortcut in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(shortcut);
                        if (IsNoiseName(name)) continue;

                        dynamic link = shellType.InvokeMember(
                            "CreateShortcut",
                            System.Reflection.BindingFlags.InvokeMethod,
                            binder: null,
                            target: shell,
                            args: new object[] { shortcut })!;

                        string? target = null;
                        try { target = link.TargetPath as string; } catch { /* atalho especial (UWP) */ }
                        Add(found, name, target, "Menu Iniciar");
                    }
                    catch
                    {
                        // atalho partido/Windows Store: ignora
                    }
                }
            }
        }
        finally
        {
            if (shell is not null)
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { /* no-op */ }
            }
        }
    }

    private static void Add(
        Dictionary<string, InstalledProgramOption> found,
        string? displayName,
        string? executablePath,
        string source)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(executablePath)) return;

        var name = displayName.Trim();
        if (name.Length < 2 || IsNoiseName(name)) return;

        var exe = executablePath.Trim().Trim('"');
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        if (IsNoiseExecutable(exe)) return;

        string full;
        try { full = Path.GetFullPath(exe); }
        catch { return; }

        if (!File.Exists(full)) return;

        if (found.TryGetValue(full, out var existing))
        {
            // Um nome de registo costuma ser mais limpo do que o nome do atalho.
            if (SourceRank(source) > SourceRank(existing.Source) ||
                (existing.DisplayName.Length < name.Length && SourceRank(source) == SourceRank(existing.Source)))
            {
                found[full] = new InstalledProgramOption(name, full, source);
            }
            return;
        }

        found[full] = new InstalledProgramOption(name, full, source);
    }

    private static int SourceRank(string source) => source switch
    {
        "Registo" => 3,
        "App Paths" => 2,
        "Menu Iniciar" => 1,
        _ => 0
    };

    /// <summary>Extrai o .exe de um DisplayIcon ("C:\...\app.exe,0" / com aspas).</summary>
    internal static string? ExeFromDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().Trim('"');
        var comma = v.LastIndexOf(',');
        if (comma > 0)
        {
            var suffix = v[(comma + 1)..].Trim();
            if (suffix.All(char.IsDigit)) v = v[..comma];
        }
        v = v.Trim().Trim('"');
        return v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? v : null;
    }

    /// <summary>Procura um .exe na pasta de instalação, preferindo o nome do programa.</summary>
    internal static string? FindExecutableIn(string? installLocation, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) return null;
        string? best = null;
        var bestScore = -1;
        var wanted = Normalize(displayName ?? string.Empty);

        try
        {
            foreach (var exe in Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly))
            {
                if (IsNoiseExecutable(exe)) continue;
                var candidate = Normalize(Path.GetFileNameWithoutExtension(exe));
                var score = 0;
                if (wanted.Length > 0 && candidate.Length > 0)
                {
                    if (wanted == candidate) score = 100;
                    else if (wanted.Contains(candidate, StringComparison.Ordinal) ||
                             candidate.Contains(wanted, StringComparison.Ordinal)) score = 60;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = exe;
                }
            }
        }
        catch
        {
            return null;
        }

        return best;
    }

    internal static bool IsNoiseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var trimmed = name.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith("Update for", StringComparison.OrdinalIgnoreCase)) return true;
        var normalized = Normalize(trimmed);
        return NoiseNameFragments.Any(f => normalized.Contains(f, StringComparison.Ordinal));
    }

    internal static bool IsNoiseExecutable(string path)
    {
        var file = Path.GetFileName(path).ToLowerInvariant();
        if (file.Length == 0) return true;
        return NoiseExeFragments.Any(f => file.Contains(f, StringComparison.Ordinal));
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [SupportedOSPlatform("windows")]
    private static string? Str(RegistryKey key, string name)
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
