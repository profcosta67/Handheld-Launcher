using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Platform;

public static class LosslessScalingService
{
    // --- Teclas ---
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU    = 0x12; // Alt
    private const byte VK_S       = 0x53;

    // --- SendInput ---
    private const uint INPUT_KEYBOARD    = 1;
    private const uint KEYEVENTF_KEYUP   = 0x0002;

    // --- Timing (humano) ---
    private const int ModifierHoldMs = 20;
    private const int KeyTapHoldMs   = 30;
    private const int PostFocusMs    = 80;

    // HWND do jogo registrado (chamado por SetGameHwnd quando o jogo é detectado)
    private static IntPtr _gameHwnd = IntPtr.Zero;

    // =====================================================================
    //  P/Invoke — INPUT com union completa (MOUSEINPUT garante 40 bytes no x64)
    // =====================================================================
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT    mi;
        [FieldOffset(0)] public KEYBDINPUT    ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int    dx;
        public int    dy;
        public uint   mouseData;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint   dwFlags;
        public uint   time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint   uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // =====================================================================
    //  API pública
    // =====================================================================

    /// <summary>
    /// Registra o HWND do jogo pra restaurar foco antes de enviar o atalho.
    /// Chamar quando a janela do jogo é detectada (JOGO-WATCHER) ou antes do toggle.
    /// </summary>
    public static void SetGameHwnd(IntPtr hwnd)
    {
        _gameHwnd = hwnd;
        if (hwnd != IntPtr.Zero)
            DebugLog.Write($"[PATINHO] GameHwnd registrado: 0x{hwnd:X}");
    }

    public static string GetExecutablePath()
    {
        var settings = AppSettingsStore.Load();
        if (!string.IsNullOrEmpty(settings.LosslessScalingPath) && File.Exists(settings.LosslessScalingPath))
            return settings.LosslessScalingPath;

        var steamPath = @"C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe";
        if (File.Exists(steamPath)) return steamPath;

        var steamPathAlt = @"C:\Program Files\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe";
        if (File.Exists(steamPathAlt)) return steamPathAlt;

        return string.Empty;
    }

    /// <summary>True se o LosslessScaling.exe está rodando.</summary>
    public static bool IsLosslessRunning()
    {
        try { return Process.GetProcessesByName("LosslessScaling").Length > 0; }
        catch { return false; }
    }

    /// <summary>
/// Se o Lossless não estiver rodando, tenta abrir. Retorna true se já estava
/// rodando ou se o launch foi disparado com sucesso. Não espera o processo
/// aparecer no Process.GetProcesses — quem chama deve aguardar.
/// </summary>
public static bool TryLaunchLossless()
{
    if (IsLosslessRunning()) return true;

    var exe = GetExecutablePath();
    if (string.IsNullOrEmpty(exe))
    {
        DebugLog.Write("[PATINHO] Caminho do Lossless não configurado — não dá pra abrir.");
        return false;
    }

    try
    {
        Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
        DebugLog.Write($"[PATINHO] Lossless iniciado automaticamente: {exe}");
        return true;
    }
    catch (Exception ex)
    {
        DebugLog.Write($"[PATINHO] Falha ao iniciar Lossless: {ex.Message}");
        return false;
    }
}

    /// <summary>
    /// Ativa/desativa o frame gen do Lossless Scaling.
    /// Fluxo: verifica processo → foca jogo → envia Ctrl+Alt+S via SendInput com timing humano.
    /// </summary>
    public static bool ToggleFrameGen()
    {
        try
        {
            // 1) Lossless tem que estar rodando
            if (!IsLosslessRunning())
            {
                DebugLog.Write("[PATINHO] LosslessScaling.exe não está em execução — abortando toggle.");
                return false;
            }

            // 2) Restaurar foco no jogo (crítico quando o overlay acabou de fechar)
            var gameHwnd = _gameHwnd;
            if (gameHwnd == IntPtr.Zero)
                gameHwnd = GetForegroundWindow();

            if (gameHwnd != IntPtr.Zero && IsWindow(gameHwnd))
            {
                GetWindowThreadProcessId(gameHwnd, out var pid);
                var isOwn = pid == (uint)Environment.ProcessId;
                if (isOwn)
                {
                    DebugLog.Write($"[PATINHO] Aviso: foreground 0x{gameHwnd:X} é do próprio launcher/overlay.");
                    // Se o launcher/overlay está em foreground, não temos pra onde focar.
                    // Segue assim mesmo — o hotkey global do Lossless pode pegar.
                }
                else
                {
                    if (!SetForegroundWindow(gameHwnd))
                        DebugLog.Write($"[PATINHO] SetForegroundWindow falhou (0x{gameHwnd:X}) — seguindo.");
                    else
                        DebugLog.Write($"[PATINHO] Foco restaurado: 0x{gameHwnd:X}");
                }
                Thread.Sleep(PostFocusMs);
            }
            else
            {
                DebugLog.Write($"[PATINHO] HWND inválido (0x{gameHwnd:X}) — seguindo sem foco.");
            }

            // 3) Enviar o atalho
            var ok = SendCtrlAltS();
            if (ok)
                DebugLog.Write("[PATINHO] Ctrl+Alt+S enviado — verifique visualmente se o frame gen ativou.");
            return ok;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Exceção no toggle: {ex.Message}");
            return false;
        }
    }

    // =====================================================================
    //  Envio do atalho
    // =====================================================================

    private static bool SendCtrlAltS()
    {
        try
        {
            // Sequência humana:
            //   [CTRL↓ ALT↓] → 20ms → [S↓] → 30ms → [S↑] → 20ms → [ALT↑ CTRL↑]
            if (!SendKeyBatch(new[] { (VK_CONTROL, true), (VK_MENU, true) })) return false;
            Thread.Sleep(ModifierHoldMs);

            if (!SendKeyBatch(new[] { (VK_S, true) })) return false;
            Thread.Sleep(KeyTapHoldMs);
            if (!SendKeyBatch(new[] { (VK_S, false) })) return false;
            Thread.Sleep(ModifierHoldMs);

            return SendKeyBatch(new[] { (VK_MENU, false), (VK_CONTROL, false) });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] SendInput exception: {ex.Message}");
            return false;
        }
    }

    private static bool SendKeyBatch((byte vk, bool down)[] keys)
    {
        var inputs = new INPUT[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk        = keys[i].vk,
                        wScan      = 0,
                        dwFlags    = keys[i].down ? 0u : KEYEVENTF_KEYUP,
                        time       = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            DebugLog.Write($"[PATINHO] SendInput parcial: {sent}/{inputs.Length} (Win32Error={Marshal.GetLastWin32Error()})");
            return false;
        }
        return true;
    }
}