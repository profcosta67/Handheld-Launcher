using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Platform;

/// <summary>
/// Suspende/resume o processo do JOGO enquanto o overlay está aberto.
/// Input do gamepad fica bloqueado (o processo suspenso não lê nada).
/// CUIDADO: o jogo fica congelado visualmente enquanto overlay está aberto.
///
/// Segurança:
///  - Só suspende se gamePid != 0, != launcher, != processos do sistema
///  - Escreve PID em arquivo pra recovery se o launcher crashar
///  - No boot, se encontrar arquivo, tenta resumir PID órfão
/// </summary>
public static class InputBlockService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    private static readonly string SuspendMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HandheldLauncher", "suspended.pid");

    private static IntPtr _suspendedHandle = IntPtr.Zero;
    private static int _suspendedPid = 0;

    private static readonly string[] BlockedProcessNames = new[]
    {
        "explorer", "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "system", "idle"
    };

    /// <summary>
    /// Suspend o processo do jogo (bloqueia input). Chamado ao abrir o overlay.
    /// Recebe o PID do JOGO (não do launcher/overlay).
    /// </summary>
    public static void BlockGamepadForGame(int gamePid)
    {
        if (!OperatingSystem.IsWindows()) return;

        // Validações de segurança — NUNCA suspender nada suspeito
        if (gamePid <= 0)
        {
            DebugLog.Write("[INPUT] Bloqueio abortado: gamePid inválido.");
            return;
        }

        if (gamePid == Environment.ProcessId)
        {
            DebugLog.Write("[INPUT] Bloqueio abortado: gamePid == launcher (não pode se auto-suspender).");
            return;
        }

        if (_suspendedHandle != IntPtr.Zero)
        {
            DebugLog.Write("[INPUT] Bloqueio já ativo, ignorando.");
            return;
        }

        try
        {
            var proc = Process.GetProcessById(gamePid);
            if (proc.HasExited)
            {
                DebugLog.Write("[INPUT] Bloqueio abortado: processo já morreu.");
                return;
            }

            // Dupla checagem de PID
            if (proc.Id == Environment.ProcessId)
            {
                DebugLog.Write("[INPUT] Bloqueio abortado: proc.Id == launcher (safety check).");
                return;
            }

            var procName = proc.ProcessName.ToLowerInvariant();
            if (Array.IndexOf(BlockedProcessNames, procName) >= 0)
            {
                DebugLog.Write($"[INPUT] Bloqueio abortado: '{procName}' é processo do sistema.");
                return;
            }

            var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)gamePid);
            if (h == IntPtr.Zero)
            {
                DebugLog.Write($"[INPUT] OpenProcess falhou (pid={gamePid}, erro={Marshal.GetLastWin32Error()}).");
                return;
            }

            var status = NtSuspendProcess(h);
            if (status != 0)
            {
                DebugLog.Write($"[INPUT] NtSuspendProcess falhou (pid={gamePid}, status=0x{status:X}).");
                CloseHandle(h);
                return;
            }

            _suspendedHandle = h;
            _suspendedPid = gamePid;
            WriteMarkerPid(gamePid);
            DebugLog.Write($"[INPUT] Jogo suspenso (pid={gamePid}, '{procName}'). Visual e áudio congelados.");
        }
        catch (ArgumentException)
        {
            DebugLog.Write($"[INPUT] Processo {gamePid} não existe mais.");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[INPUT] Block EXCEPTION: {ex.Message}");
        }
    }

    /// <summary>
    /// Resume o processo suspenso. Chamado ao fechar o overlay.
    /// </summary>
    public static void UnblockGamepadForGame()
    {
        if (!OperatingSystem.IsWindows()) return;

        if (_suspendedHandle == IntPtr.Zero && _suspendedPid == 0)
        {
            // Sem nada suspenso, mas garante que o marker sumiu
            DeleteMarker();
            return;
        }

        try
        {
            if (_suspendedHandle != IntPtr.Zero)
            {
                var status = NtResumeProcess(_suspendedHandle);
                if (status == 0)
                    DebugLog.Write($"[INPUT] Jogo resumido (pid={_suspendedPid}).");
                else
                    DebugLog.Write($"[INPUT] NtResumeProcess retornou 0x{status:X} (pid={_suspendedPid}).");

                CloseHandle(_suspendedHandle);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[INPUT] Resume EXCEPTION: {ex.Message}");
        }
        finally
        {
            _suspendedHandle = IntPtr.Zero;
            _suspendedPid = 0;
            DeleteMarker();
        }
    }

    /// <summary>
    /// Chamado no boot do launcher. Se encontrar marker de PID órfão,
    /// tenta resumir o processo (recovery de crash anterior).
    /// </summary>
    public static void RecoverOrphanedSuspension()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (!File.Exists(SuspendMarkerPath)) return;

            var content = File.ReadAllText(SuspendMarkerPath).Trim();
            if (!int.TryParse(content, out var pid) || pid <= 0)
            {
                DeleteMarker();
                return;
            }

            if (pid == Environment.ProcessId)
            {
                DebugLog.Write("[INPUT-RECOVERY] Marker aponta pro próprio launcher, ignorando.");
                DeleteMarker();
                return;
            }

            var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)pid);
            if (h != IntPtr.Zero)
            {
                NtResumeProcess(h);
                CloseHandle(h);
                DebugLog.Write($"[INPUT-RECOVERY] PID órfão {pid} resumido (crash anterior do launcher).");
            }
            else
            {
                DebugLog.Write($"[INPUT-RECOVERY] Processo {pid} não está mais rodando.");
            }

            DeleteMarker();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[INPUT-RECOVERY] EXCEPTION: {ex.Message}");
            DeleteMarker();
        }
    }

    private static void WriteMarkerPid(int pid)
    {
        try
        {
            var dir = Path.GetDirectoryName(SuspendMarkerPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SuspendMarkerPath, pid.ToString());
        }
        catch (Exception ex) { DebugLog.Write($"[INPUT-RECOVERY] Falha ao gravar marcador de suspensão (PID {pid}): {ex.Message}"); }
    }

    private static void DeleteMarker()
    {
        try { if (File.Exists(SuspendMarkerPath)) File.Delete(SuspendMarkerPath); }
        catch (Exception ex) { DebugLog.Write($"[INPUT-RECOVERY] Falha ao remover marcador de suspensão: {ex.Message}"); }
    }
}