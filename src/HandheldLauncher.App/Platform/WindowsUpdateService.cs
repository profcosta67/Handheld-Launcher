using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Platform;

public class WindowsUpdateService
{
    public async Task<List<SystemUpdateItem>> SearchForUpdatesAsync()
    {
        return await Task.Run(() =>
        {
            var updates = new List<SystemUpdateItem>();
            try
            {
                Type updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")!;
                dynamic updateSession = Activator.CreateInstance(updateSessionType)!;
                dynamic updateSearcher = updateSession.CreateUpdateSearcher();

                // 🟢 AQUI ESTÁ A MÁGICA: Busca Software E Drivers
                dynamic searchResult = updateSearcher.Search("IsInstalled=0 and (Type='Software' or Type='Driver')");

                foreach (var update in searchResult.Updates)
                {
                    updates.Add(new SystemUpdateItem
                    {
                        Title = update.Title,
                        Description = update.Description,
                        StatusText = "Disponível"
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[WindowsUpdate] Erro ao buscar atualizações: {ex.Message}");
            }
            return updates;
        });
    }

    public async Task<bool> DownloadAndInstallUpdatesAsync(List<SystemUpdateItem> itemsToInstall, Action<double, string> onProgress)
    {
        return await Task.Run(() =>
        {
            try
            {
                Type updateSessionType = Type.GetTypeFromProgID("Microsoft.Update.Session")!;
                dynamic updateSession = Activator.CreateInstance(updateSessionType)!;
                dynamic updateSearcher = updateSession.CreateUpdateSearcher();
                
                // 🟢 Mantém a mesma busca para garantir que pegamos os objetos corretos
                dynamic searchResult = updateSearcher.Search("IsInstalled=0 and (Type='Software' or Type='Driver')");

                Type updateCollType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!;
                dynamic updateColl = Activator.CreateInstance(updateCollType)!;

                foreach (var update in searchResult.Updates)
                {
                    if (itemsToInstall.Exists(i => i.Title == update.Title))
                    {
                        updateColl.Add(update);
                    }
                }

                if (updateColl.Count == 0) return false;

                // Download
                onProgress?.Invoke(0, "Baixando atualizações e drivers...");
                dynamic downloader = updateSession.CreateUpdateDownloader();
                downloader.Updates = updateColl;
                downloader.Download();

                // Instalação
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
                DebugLog.Write($"[WindowsUpdate] Erro ao instalar: {ex.Message}");
                return false;
            }
        });
    }
}