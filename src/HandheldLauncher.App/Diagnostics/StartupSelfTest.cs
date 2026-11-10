using System;
using System.Reflection;
using HandheldLauncher.App.Services;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// Autoteste de arranque sem interface gráfica (<c>--selftest</c>).
///
/// Existe por causa do beta.5: o pacote Linux foi gerado, extraído, instalado
/// e verificado com sucesso, mas a aplicação morria no arranque com
/// <c>FileNotFoundException: LibreHardwareMonitorLib</c> — o pacote NuGet só
/// publica binários para Windows, logo o publish <c>-r linux-x64</c> não o
/// copiava. O teste do instalador nunca abria a aplicação, por isso o defeito
/// só apareceu ao utilizador final.
///
/// Aqui, sem abrir janela nem tocar em Avalonia:
/// 1. carrega no processo todas as assemblies referenciadas pelo launcher
///    (é exactamente a resolução que o runtime faz ao arrancar);
/// 2. constrói os serviços que o arranque cria — um construtor que rebente
///    (P/Invoke de Windows, driver em falta, mapping inexistente) derrubaria
///    a janela antes de aparecer.
///
/// Sai com 0 se tudo carregar e 1 caso contrário, para o CI poder falhar.
/// </summary>
internal static class StartupSelfTest
{
    public const string Argument = "--selftest";

    public static int Run()
    {
        var failures = 0;

        Console.WriteLine("== Handheld Launcher — autoteste de arranque ==");
        Console.WriteLine($"   Plataforma: {Environment.OSVersion.Platform} | .NET {Environment.Version}");

        // 1) Assemblies referenciadas — a falha do beta.5 era exactamente esta.
        foreach (var reference in typeof(StartupSelfTest).Assembly.GetReferencedAssemblies())
        {
            try
            {
                _ = Assembly.Load(reference);
                Console.WriteLine($"   ok   assembly {reference.Name} {reference.Version}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"   FALHA assembly {reference.Name}: {ex.GetType().Name} — {ex.Message}");
            }
        }

        // 2) Serviços construídos no arranque.
        failures += Check("monitor de hardware", () =>
        {
            using var monitor = HardwareMonitor.Create();
            _ = monitor.GetCpuTemperature();
        });

        failures += Check("leitura HWiNFO (opcional)", () =>
        {
            using var hwInfo = new HWiNFOSharedMemoryService();
            _ = hwInfo.TryRead(out _);
        });

        if (failures == 0)
        {
            Console.WriteLine("== AUTOTESTE OK ==");
            return 0;
        }

        Console.Error.WriteLine($"== AUTOTESTE FALHOU ({failures} problema(s)) ==");
        return 1;
    }

    private static int Check(string what, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"   ok   {what}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"   FALHA {what}: {ex.GetType().Name} — {ex.Message}");
            return 1;
        }
    }
}
