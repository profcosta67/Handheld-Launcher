using System.Runtime.InteropServices;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Liga duas configurações do PRÓPRIO RTSS que até a v0.9.3 pedíamos pro
/// usuário ajustar na mão, na primeira vez (ver dica na tela de
/// Configurações / README "Configurar o RTSS"):
/// <list type="bullet">
/// <item><b>EnableOSD = 1</b> ("Show On-Screen Display") — sem isso o nosso
/// HUD (escrito via <see cref="RtssSharedMemory"/>) nunca aparece, mesmo com
/// o RTSS rodando.</item>
/// <item><b>EnableStat = 0</b> ("Show own statistics") — senão o contador de
/// FPS NATIVO do RTSS aparece junto com o nosso, duplicado (bug reportado
/// pelo usuário).</item>
/// </list>
/// Usa a mesma API oficial de perfis do RTSS que o MSI Afterburner e
/// ferramentas de automação como o rtss-cli (github.com/xanderfrangos/
/// rtss-cli) usam: carrega "RTSSHooks64.dll" — um arquivo que já existe
/// dentro da PRÓPRIA pasta de instalação do RTSS — e chama as funções
/// exportadas Load/Save/SetProfileProperty/UpdateProfiles documentadas pelo
/// criador do RTSS ("Unwinder"). Perfil "" (string vazia) = perfil GLOBAL.
///
/// Importante: isso NÃO escreve nenhum arquivo em "Program Files" (só
/// chama funções de uma DLL que já está lá, só pra leitura/execução) — por
/// isso não precisa do launcher rodando como admin, igual o resto do app.
///
/// Só toca nessas DUAS propriedades, só no perfil global, melhor esforço
/// total (nunca lança, nunca bloqueia nada). Nunca testado contra um RTSS
/// de verdade por quem escreveu — se uma versão futura do RTSS mudar essa
/// API e isso parar de funcionar, o pior caso é continuar precisando
/// configurar na mão, igual era antes desta leva.
/// </summary>
internal static class RtssProfileConfig
{
    private const string GlobalProfile = "";
    private const string PropEnableOsd = "EnableOSD";
    private const string PropEnableStat = "EnableStat";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void LoadProfileDelegate([MarshalAs(UnmanagedType.LPStr)] string profile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SaveProfileDelegate([MarshalAs(UnmanagedType.LPStr)] string profile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool SetProfilePropertyDelegate([MarshalAs(UnmanagedType.LPStr)] string property, ref int data, int size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UpdateProfilesDelegate();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    /// <summary>
    /// Tenta configurar o perfil global do RTSS. <paramref name="rtssInstallDir"/>
    /// é a pasta onde o RTSS.exe está instalado (ver
    /// PerformanceOverlayService.FindInstalledExecutable). Retorna true só
    /// se conseguiu carregar a DLL e achar todas as funções — não é
    /// garantia de que o RTSS já aplicou (best-effort).
    /// </summary>
    public static bool TryConfigureGlobalOsd(string rtssInstallDir)
    {
        var dllPath = Path.Combine(rtssInstallDir, "RTSSHooks64.dll");
        if (!File.Exists(dllPath)) return false;

        var hModule = IntPtr.Zero;
        try
        {
            hModule = LoadLibraryW(dllPath);
            if (hModule == IntPtr.Zero) return false;

            var loadProfile = GetDelegate<LoadProfileDelegate>(hModule, "LoadProfile");
            var saveProfile = GetDelegate<SaveProfileDelegate>(hModule, "SaveProfile");
            var setProp = GetDelegate<SetProfilePropertyDelegate>(hModule, "SetProfileProperty");
            var updateProfiles = GetDelegate<UpdateProfilesDelegate>(hModule, "UpdateProfiles");

            if (loadProfile is null || saveProfile is null || setProp is null || updateProfiles is null)
                return false;

            loadProfile(GlobalProfile);

            var osdOn = 1;
            setProp(PropEnableOsd, ref osdOn, sizeof(int));

            var statOff = 0;
            setProp(PropEnableStat, ref statOff, sizeof(int));

            saveProfile(GlobalProfile);
            updateProfiles();

            return true;
        }
        catch
        {
            return false; // Melhor esforço — nunca derruba o launcher por isso.
        }
        finally
        {
            if (hModule != IntPtr.Zero)
            {
                try { FreeLibrary(hModule); } catch { /* melhor esforço */ }
            }
        }
    }

    private static T? GetDelegate<T>(IntPtr hModule, string exportName) where T : Delegate
    {
        var ptr = GetProcAddress(hModule, exportName);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
}
