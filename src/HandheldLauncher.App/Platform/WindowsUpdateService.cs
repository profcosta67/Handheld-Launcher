using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.App.Platform;

public class WindowsUpdateService
{
    // v0.16.3: o critério antigo "IsInstalled=0 and (Type='Software' or Type='Driver')"
    // é INVÁLIDO pro Windows Update — o OR só pode ficar no nível de cima
    // ("(x and y) or (z)"), nunca dentro de um AND. Toda busca dava
    // 0x80240032 (WU_E_INVALID_CRITERIA) e a lista voltava vazia.
    // IsHidden=0: não mostrar o que o usuário ocultou no Windows.
    private const string SearchCriteria =
        "(IsInstalled=0 and IsHidden=0 and Type='Software') or (IsInstalled=0 and IsHidden=0 and Type='Driver')";

    // Critério inválido é bug, não erro transitório — não adianta tentar de novo.
    private const int WU_E_INVALID_CRITERIA = unchecked((int)0x80240032);

    // HRESULTs transitórios (boot, rede subindo, serviço parado)
    private const int WU_E_SERVICE_STOP     = unchecked((int)0x8024002E);
    private const int WU_E_PT_WINHTTP_NOT_RESOLVED = unchecked((int)0x8024402C);
    private const int WU_E_NOT_READY        = unchecked((int)0x80240438);

    private static bool IsTransient(int hr) =>
        hr == WU_E_SERVICE_STOP
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
                dynamic searchResult = updateSearcher.Search(SearchCriteria);
                foreach (var update in searchResult.Updates)
                {
                    items.Add(new SystemUpdateItem
                    {
                        Title = update.Title,
                        Description = update.Description,
                        StatusText = "Disponível"
                    });
                }
                DebugLog.Write($"[WindowsUpdate] Busca ok: {items.Count} atualização(ões) disponível(is).");
                return (true, false, items);
            }
            catch (Exception ex)
            {
                int hr = ex.HResult;
                bool transient = IsTransient(hr);
                if (hr == WU_E_INVALID_CRITERIA)
                    DebugLog.Write("[WindowsUpdate] Critério de busca inválido (bug do launcher).");
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
                dynamic searchResult = updateSearcher.Search(SearchCriteria);

                Type updateCollType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!;
                dynamic updateColl = Activator.CreateInstance(updateCollType)!;

                foreach (var update in searchResult.Updates)
                {
                    if (itemsToInstall.Exists(i => i.Title == update.Title))
                        updateColl.Add(update);
                }

                if (updateColl.Count == 0)
                {
                    DebugLog.Write("[WindowsUpdate] Nada pra instalar (os itens escolhidos não estão mais na busca).");
                    return false;
                }
                DebugLog.Write($"[WindowsUpdate] Baixando e instalando {updateColl.Count} item(ns)...");

                onProgress?.Invoke(0, "Baixando atualizações e drivers...");
                dynamic downloader = updateSession.CreateUpdateDownloader();
                downloader.Updates = updateColl;
                downloader.Download();

                onProgress?.Invoke(50, "Instalando... (a tela pode piscar)");
                dynamic installer = updateSession.CreateUpdateInstaller();
                installer.Updates = updateColl;
                dynamic installResult = installer.Install();

                bool rebootRequired = installResult.RebootRequired;

                // v0.16.3: registrar o resultado de cada item. Códigos do
                // Windows Update: 2 = ok, 3 = ok com erros, 4 = falhou, 5 = abortado.
                int overall = installResult.ResultCode;
                DebugLog.Write($"[WindowsUpdate] Instalação terminou: resultado={overall}, reinício necessário={rebootRequired}, itens={updateColl.Count}.");
                for (int i = 0; i < updateColl.Count; i++)
                {
                    try
                    {
                        dynamic r = installResult.GetUpdateResult(i);
                        string title = updateColl.Item(i).Title;
                        int code = r.ResultCode;
                        int hr = r.HResult;
                        DebugLog.Write($"[WindowsUpdate]   '{title}': resultado={code}, hr=0x{hr:X8}");
                    }
                    catch (Exception itemEx)
                    {
                        DebugLog.Write($"[WindowsUpdate]   item {i}: não consegui ler o resultado ({itemEx.Message})");
                    }
                }
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