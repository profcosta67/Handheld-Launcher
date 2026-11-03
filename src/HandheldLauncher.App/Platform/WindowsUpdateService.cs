using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Platform;

public class WindowsUpdateService
{
    // HRESULTs transitórios (boot, rede subindo, serviço parado)
    private const int WU_E_INVALID_CRITERIA = unchecked((int)0x80240032);
    private const int WU_E_SERVICE_STOP     = unchecked((int)0x8024002E);
    private const int WU_E_PT_WINHTTP_NOT_RESOLVED = unchecked((int)0x8024402C);
    private const int WU_E_NOT_READY        = unchecked((int)0x80240438);

    private static bool IsTransient(int hr) =>
        hr == WU_E_INVALID_CRITERIA || hr == WU_E_SERVICE_STOP
        || hr == WU_E_PT_WINHTTP_NOT_RESOLVED || hr == WU_E_NOT_READY;

    public async Task<List<SystemUpdateItem>> SearchForUpdatesAsync()
    {
        var (ok, transient, items) = await TrySearchOnceAsync();
        if (ok) return items;

        if (!transient)
        {
            DebugLog.Write("[WindowsUpdate] Erro permanente — retorno vazio.");
            return new List<SystemUpdateItem>();
        }

        DebugLog.Write("[WindowsUpdate] Erro transitório, retentando em 5s...");
        await Task.Delay(5000);

        (ok, transient, items) = await TrySearchOnceAsync();
        if (ok) return items;

        DebugLog.Write("[WindowsUpdate] 2 tentativas falharam (transitório persistente). Retorno vazio.");
        return new List<SystemUpdateItem>();
    }

    private async Task<(bool Ok, bool Transient, List<SystemUpdateItem> Items)> TrySearchOnceAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<SystemUpdateItem>();
            try
            {
                Type updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")!;
                dynamic updateSession = Activator.CreateInstance(updateSessionType)!;
                dynamic updateSearcher = updateSession.CreateUpdateSearcher();
                dynamic searchResult = updateSearcher.Search("IsInstalled=0 and (Type='Software' or Type='Driver')");
                foreach (var update in searchResult.Updates)
                {
                    items.Add(new SystemUpdateItem
                    {
                        Title = update.Title,
                        Description = update.Description,
                        StatusText = "Disponível"
                    });
                }
                return (true, false, items);
            }
            catch (Exception ex)
            {
                int hr = ex.HResult;
                bool transient = IsTransient(hr);
                DebugLog.Write($"[WindowsUpdate] Tentativa falhou (HR=0x{hr:X8}, transitório={transient}): {ex.Message}");
                return (false, transient, items);
            }
        });
    }

    public async Task<bool> DownloadAndInstallUpdatesAsync(List<SystemUpdateItem> itemsToInstall, Action<double, string> onProgress)
    {
        // (mantém o código atual — instalação é ação do usuário, não retry silencioso)
        return await Task.Run(() =>
        {
            try
            {
                Type updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")!;
                dynamic updateSession = Activator.CreateInstance(updateSessionType)!;
                dynamic updateSearcher = updateSession.CreateUpdateSearcher();
                dynamic searchResult = updateSearcher.Search("IsInstalled=0 and (Type='Software' or Type='Driver')");

                Type updateCollType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!;
                dynamic updateColl = Activator.CreateInstance(updateCollType)!;

                foreach (var update in searchResult.Updates)
                {
                    if (itemsToInstall.Exists(i => i.Title == update.Title))
                        updateColl.Add(update);
                }

                if (updateColl.Count == 0) return false;

                onProgress?.Invoke(0, "Baixando atualizações e drivers...");
                dynamic downloader = updateSession.CreateUpdateDownloader();
                downloader.Updates = updateColl;
                downloader.Download();

                onProgress?.Invoke(50, "Instalando... (a tela pode piscar)");
                dynamic installer = updateSession.CreateUpdateInstaller();
                installer.Updates = updateColl;
                dynamic installResult = installer.Install();

                bool rebootRequired = installResult.RebootRequired;
                onProgress?.Invoke(100, rebootRequired
                    ? "Concluído! Reinicie o aparelho para finalizar."
                    : "Concluído! Tudo instalado.");

                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[WindowsUpdate] Erro ao instalar (HR=0x{ex.HResult:X8}): {ex.Message}");
                return false;
            }
        });
    }
}