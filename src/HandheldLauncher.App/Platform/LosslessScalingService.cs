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
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_MINIMIZE = 6;
    private const int SW_SHOWMINNOACTIVE = 7;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // P/Invokes adicionados para o FIX P2 (Minimizar mais rápido)
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    // v0.16.3 (P2 — "tela cinza" na 1ª ativação): título da janela principal
    // do Lossless, como aparece no debug.log ([JOGO-WATCHER] Janelas visíveis).
    private const string LosslessMainWindowTitle = "Lossless Scaling";
    private static readonly TimeSpan MinimizeWatchTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MinimizeSettleTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan GuardGrace = TimeSpan.FromSeconds(60);
    private const int GuardPollMs = 500;
    private static int _guardRunning;

    /// <summary>
    /// v0.16.3 — P2: a janela do Lossless (interface cinza escura) às vezes
    /// ficava aberta na frente do jogo depois de o launcher abrir o Lossless.
    /// Log real: "Lossless iniciado" sem o "minimizada" depois. O vigia antigo
    /// procurava só por 6s, só no processo original e parava na primeira janela
    /// — falhava se o Lossless demorasse a criar a janela (1ª abertura com jogo
    /// pesado), se mostrasse uma tela de carregamento antes da principal, ou se
    /// reiniciasse a si mesmo (o processo original some), e desistia sem log.
    ///
    /// Agora: procura por até 20s em qualquer processo LosslessScaling; minimiza
    /// SÓ janelas visíveis com o título exato da janela principal (nunca a
    /// janela da imagem escalada, que o frame gen cria); continua 3s depois de
    /// minimizar pra pegar uma reabertura; e registra quando não achar nada.
    /// </summary>
    private static void MinimizeLosslessMainWindow()
    {
        try
        {
            var started = DateTime.UtcNow;
            DateTime? lastHandled = null;
            var minimizedCount = 0;
            var sawWindow = false;

            while (DateTime.UtcNow - started < MinimizeWatchTimeout)
            {
                if (lastHandled is { } last && DateTime.UtcNow - last > MinimizeSettleTime) break;

                var minimizedNow = MinimizeMainWindows(noActivate: false, out var found);
                if (found) { sawWindow = true; lastHandled ??= DateTime.UtcNow; }
                if (minimizedNow > 0)
                {
                    minimizedCount += minimizedNow;
                    lastHandled = DateTime.UtcNow;
                    DebugLog.Write($"[PATINHO] Janela do Lossless minimizada com sucesso ({(DateTime.UtcNow - started).TotalSeconds:0.0}s após abrir).");
                }

                Thread.Sleep(100);
            }

            if (minimizedCount == 0 && sawWindow)
                DebugLog.Write("[PATINHO] Janela do Lossless já estava minimizada (aberto em segundo plano) — nada a fazer.");
            else if (minimizedCount == 0)
                DebugLog.Write($"[PATINHO] Não encontrei a janela principal do Lossless em {MinimizeWatchTimeout.TotalSeconds:0}s — ela pode ter ficado aberta na tela.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Não foi possível minimizar a janela: {ex.Message}");
        }

        StartMainWindowGuard();
    }

    /// <summary>
    /// Minimiza as janelas principais do Lossless que estiverem visíveis e não
    /// minimizadas. <paramref name="found"/> diz se achou alguma janela principal
    /// (mesmo já minimizada) — assim "já estava minimizada" não vira falso alarme.
    /// Com <paramref name="noActivate"/> usa SW_SHOWMINNOACTIVE, que não rouba o
    /// foco do jogo.
    /// </summary>
    private static int MinimizeMainWindows(bool noActivate, out bool found)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("LosslessScaling"))
        {
            using (p) { try { pids.Add((uint)p.Id); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); } }
        }

        found = false;
        if (pids.Count == 0) return 0;

        var targets = new List<IntPtr>();
        var sawAny = false;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!pids.Contains(pid) || !IsWindowVisible(hWnd)) return true;
            var title = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);
            if (!string.Equals(title.ToString(), LosslessMainWindowTitle, StringComparison.Ordinal)) return true;
            sawAny = true;
            // v0.16.3: janela minimizada continua "visível" pro Windows — sem
            // pular as minimizadas, o vigia minimizava a mesma janela a cada 100 ms.
            if (!IsIconic(hWnd)) targets.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        found = sawAny;
        foreach (var hwnd in targets)
            ShowWindow(hwnd, noActivate ? SW_SHOWMINNOACTIVE : SW_MINIMIZE);
        return targets.Count;
    }

    /// <summary>
    /// Guarda contínuo: enquanto um jogo aberto pelo launcher estiver rodando (e
    /// por 60 s depois de abrir o Lossless), se a janela principal do Lossless
    /// reaparecer na frente — o Lossless às vezes se reabre sozinho —, minimiza de
    /// novo sem tirar o foco do jogo. Fora de uma sessão de jogo não mexe em nada,
    /// então abrir o Lossless à mão para configurar continua funcionando.
    /// </summary>
    private static void StartMainWindowGuard()
    {
        if (Interlocked.Exchange(ref _guardRunning, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var begin = DateTime.UtcNow;
            var reminimized = 0;
            try
            {
                while (true)
                {
                    var gameAlive = _gameHwnd != IntPtr.Zero && IsWindow(_gameHwnd);
                    if (!gameAlive && DateTime.UtcNow - begin > GuardGrace) break;

                    var n = MinimizeMainWindows(noActivate: true, out _);
                    if (n > 0)
                    {
                        reminimized += n;
                        if (reminimized <= 3)
                            DebugLog.Write("[PATINHO] Janela do Lossless reapareceu — minimizada de novo (sem tirar o foco do jogo).");
                    }
                    Thread.Sleep(GuardPollMs);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[PATINHO] Guarda da janela do Lossless parou: {ex.Message}");
            }
            finally
            {
                if (reminimized > 3)
                    DebugLog.Write($"[PATINHO] Janela do Lossless minimizada de novo {reminimized}x nesta sessão.");
                Interlocked.Exchange(ref _guardRunning, 0);
            }
        });
    }

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
        {
            DebugLog.Write($"[PATINHO] GameHwnd registrado: 0x{hwnd:X}");
            // Lossless pode já estar aberto (Steam, início do Windows): vigia a janela dele.
            if (IsLosslessRunning()) StartMainWindowGuard();
        }
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
    /// rodando ou se o launch foi disparado com sucesso.
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
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            });
            DebugLog.Write($"[PATINHO] Lossless iniciado automaticamente: {exe}");

            process?.Dispose();
            // O vigia procura pelo nome do processo, então funciona mesmo se o
            // Start não devolver o processo (shell reaproveitando instância) ou
            // se o Lossless reiniciar a si mesmo.
            ThreadPool.QueueUserWorkItem(_ => MinimizeLosslessMainWindow());
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