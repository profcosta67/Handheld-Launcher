using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

public static class LosslessScalingService
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_S = 0x53;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>
    /// Procura o LosslessScaling.exe na pasta padrão da Steam ou no caminho salvo.
    /// </summary>
    public static string GetExecutablePath()
    {
        var settings = AppSettingsStore.Load();
        if (!string.IsNullOrEmpty(settings.LosslessScalingPath) && File.Exists(settings.LosslessScalingPath))
            return settings.LosslessScalingPath;

        // Tenta achar na Steam (padrão)
        var steamPath = @"C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe";
        if (File.Exists(steamPath)) return steamPath;

        var steamPathAlt = @"C:\Program Files\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe";
        if (File.Exists(steamPathAlt)) return steamPathAlt;

        return string.Empty;
    }

    /// <summary>
    /// Envia o atalho global Ctrl+Alt+S para ativar/desativar o frame gen na janela ativa.
    /// </summary>
    public static bool ToggleFrameGen()
    {
        try
        {
            DebugLog.Write("[PATINHO] Simulando Ctrl+Alt+S para ativar o Lossless Scaling.");
            
            // Simula o pressionamento das teclas
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_S, 0, 0, UIntPtr.Zero);

            // Solta as teclas
            keybd_event(VK_S, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Erro ao simular atalho: {ex.Message}");
            return false;
        }
    }
}