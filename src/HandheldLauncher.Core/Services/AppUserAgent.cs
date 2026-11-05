using System.Linq;
using System.Reflection;

namespace HandheldLauncher.Core.Services;

/// <summary>
/// v0.16.2: User-Agent único do launcher nas requisições HTTP. A versão vem
/// do assembly de entrada (que vem do arquivo VERSION na raiz, via o .csproj
/// do App) — antes, dois lugares tinham "0.14" e "15.6" fixos no código e
/// ficavam desatualizados a cada versão.
/// </summary>
public static class AppUserAgent
{
    public static string Value { get; } = $"HandheldLauncher/{ReadVersion()}";

    private static string ReadVersion()
    {
        try
        {
            return Assembly.GetEntryAssembly()?
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "dev";
        }
        catch
        {
            return "dev";
        }
    }
}
