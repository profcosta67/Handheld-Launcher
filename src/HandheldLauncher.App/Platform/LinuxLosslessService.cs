using System.Diagnostics;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Lossless Scaling no Linux = <b>LSFG-VK</b> (camada Vulkan que gera frames
/// nos jogos reutilizando o <c>Lossless.dll</c> da cópia do Lossless Scaling).
///
/// No Windows o launcher abre o <c>LosslessScaling.exe</c> e envia Ctrl+Alt+S;
/// no Linux não há app nem atalho: o frame-gen é configurado por variáveis de
/// ambiente (<c>ENABLE_LSFG</c>/<c>LSFG_MULTIPLIER</c>) que o LSFG-VK lê ao
/// criar a instância Vulkan de cada jogo.
///
/// Gerimos dois ficheiros:
///  - <c>~/.config/environment.d/90-lsfg.conf</c>: liga/desliga o frame-gen
///    para os jogos lançados numa sessão nova (é o que o systemd user aplica).
///  - <c>~/.config/lsfg-vk/conf.toml</c>: multiplicador global (lido pelo
///    próprio LSFG-VK; o Decky Framegen também escreve aqui).
/// </summary>
public static class LinuxLosslessService
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string EnvFile => Path.Combine(Home, ".config", "environment.d", "90-lsfg.conf");
    private static string ConfToml => Path.Combine(Home, ".config", "lsfg-vk", "conf.toml");

    /// <summary>A camada Vulkan do LSFG-VK está instalada?</summary>
    public static bool IsInstalled
    {
        get
        {
            var dirs = new[]
            {
                "/usr/share/vulkan/implicit_layer.d",
                "/usr/share/vulkan/explicit_layer.d",
                "/etc/vulkan/implicit_layer.d",
                Path.Combine(Home, ".local/share/vulkan/implicit_layer.d"),
                Path.Combine(Home, ".local/share/vulkan/explicit_layer.d"),
            };

            foreach (var dir in dirs)
            {
                try
                {
                    if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*lsfg*").Any())
                        return true;
                }
                catch { /* diretório sem acesso — segue */ }
            }
            return false;
        }
    }

    /// <summary>O frame-gen está ligado por env? (lê o environment.d)</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                return File.Exists(EnvFile)
                       && File.ReadAllText(EnvFile).Contains("ENABLE_LSFG=1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string GetStatus()
    {
        if (!IsInstalled) return "LSFG-VK não instalado";
        return IsEnabled
            ? "LSFG-VK pronto (frame-gen ligado)"
            : "LSFG-VK instalado (desligado)";
    }

    /// <summary>
    /// Liga/desliga o frame-gen (escreve o environment.d) e reforça o
    /// multiplicador no conf.toml. Aplica-se ao próximo jogo/sessão.
    /// </summary>
    public static bool SetEnabled(bool enabled, int multiplier)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EnvFile)!);
            if (enabled)
            {
                File.WriteAllText(EnvFile,
                    "# Gerado pelo Handheld Launcher — LSFG-VK (Lossless frame-gen)\n" +
                    "ENABLE_LSFG=1\n" +
                    $"LSFG_MULTIPLIER={Math.Clamp(multiplier, 2, 4)}\n");
            }
            else if (File.Exists(EnvFile))
            {
                File.Delete(EnvFile);
            }

            SetMultiplier(multiplier);
            DebugLog.Write($"[LSFG] Linux: frame-gen {(enabled ? "LIGADO" : "DESLIGADO")} ({multiplier}x). Reinicie a sessão/jogo para aplicar.");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LSFG] Linux: falha ao escrever {EnvFile}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Atualiza o multiplicador (2x/3x/4x) no conf.toml do LSFG-VK.</summary>
    public static bool SetMultiplier(int multiplier)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfToml)!);
            var clamped = Math.Clamp(multiplier, 2, 4);

            if (!File.Exists(ConfToml))
            {
                // Cria um conf.toml mínimo; o utilizador/Decky preenche o `dll`.
                File.WriteAllText(ConfToml,
                    "version = 1\n\n[global]\nmultiplier = " + clamped + "\n");
                DebugLog.Write($"[LSFG] Linux: conf.toml criado ({clamped}x). Indique o caminho do Lossless.dll no LSFG-VK.");
                return true;
            }

            var lines = File.ReadAllLines(ConfToml).ToList();
            var replaced = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("multiplier", StringComparison.Ordinal))
                {
                    lines[i] = "multiplier = " + clamped;
                    replaced = true;
                }
            }
            if (!replaced) lines.Add("multiplier = " + clamped);

            File.WriteAllLines(ConfToml, lines);
            DebugLog.Write($"[LSFG] Linux: multiplicador → {clamped}x.");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[LSFG] Linux: falha ao atualizar conf.toml: {ex.Message}");
            return false;
        }
    }
}