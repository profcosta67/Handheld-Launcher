using System;
using System.Runtime.InteropServices;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Real battery % for the top bar, via the plain Win32
/// GetSystemPowerStatus API (kernel32.dll) — fixes the reported bug
/// "a bateria não está acompanhando o real, ela está cravada a 100%": the
/// top bar used to just have "⚡100%" typed in as a literal placeholder
/// string in MainWindow.axaml, never actually wired to anything, which is
/// exactly why it never moved off 100%.
///
/// Deliberately NOT using System.Windows.Forms.SystemInformation.
/// PowerStatus (same underlying OS call, but would pull the whole WinForms
/// assembly into an Avalonia app just for one struct) — this calls the OS
/// directly instead, same as WinForms does internally.
/// </summary>
public static class BatteryStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    // Bits/values per the documented SYSTEM_POWER_STATUS struct.
    private const byte BatteryFlagNoSystemBattery = 128;
    private const byte BatteryLifePercentUnknown = 255;
    private const byte AcLineStatusOnline = 1;

    /// <summary>
    /// Formatted text for the top bar ("⚡86%" on battery, "🔌 86%" plugged
    /// into the wall), or "" when there's no battery at all — a desktop PC,
    /// which is also where this gets tested most of the time (BatteryFlag
    /// has the "no system battery" bit set) — or Windows can't report it
    /// right now (BatteryLifePercent == 255/unknown). Showing nothing in
    /// those cases is deliberate: better an empty spot than swapping one
    /// frozen fake number (the old "100%") for another.
    ///
    /// v0.9.8, bug reportado pelo usuário ("faltou...um icone de ligado na
    /// tomada ou não"): a versão anterior só mostrava 🔌 quando
    /// BatteryFlagCharging (bit "carregando de verdade") estava ligado —
    /// então um aparelho NA TOMADA com a bateria já CHEIA (carga completa,
    /// bit "charging" desligado porque não tem mais nada pra carregar)
    /// caía pro ⚡, como se estivesse rodando na bateria. O que o ícone
    /// precisa responder é só "está na tomada?" (ACLineStatus), não "está
    /// efetivamente empurrando corrente pra bateria agora?" — por isso o
    /// ícone agora depende só do ACLineStatus.
    /// </summary>
    public static string GetDisplayText()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status)) return string.Empty;
            if ((status.BatteryFlag & BatteryFlagNoSystemBattery) != 0) return string.Empty;
            if (status.BatteryLifePercent == BatteryLifePercentUnknown) return string.Empty;

            var pluggedIn = status.ACLineStatus == AcLineStatusOnline;
            var icon = pluggedIn ? "🔌" : "⚡";
            return $"{icon} {status.BatteryLifePercent}%";
        }
        catch
        {
            // Best-effort only — never let a battery readout crash the app.
            return string.Empty;
        }
    }
}
