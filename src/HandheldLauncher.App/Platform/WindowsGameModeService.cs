using System;
using Microsoft.Win32;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform
{
    public class WindowsGameModeService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "HandheldLauncher";

        public static void SetLauncherOnBoot(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                
                if (enable)
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                        key?.SetValue(AppName, $"\"{exePath}\"");

                    // Desativa o Xbox FSE e Game Bar para não roubar o boot
                    using var gameBarKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameBar", writable: true);
                    if (gameBarKey != null)
                    {
                        gameBarKey.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
                        gameBarKey.SetValue("ShowStartupPanel", 0, RegistryValueKind.DWord);
                    }

                    using var gameDvrKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", writable: true);
                    if (gameDvrKey != null)
                    {
                        gameDvrKey.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                    }
                }
                else
                {
                    key?.DeleteValue(AppName, throwOnMissingValue: false);
                    
                    using var gameBarKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameBar", writable: true);
                    gameBarKey?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                }

                DebugLog.Write($"[GAME MODE] Boot configurado: {enable}");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[GAME MODE] Falha ao configurar boot: {ex.Message}");
            }
        }
    }
}