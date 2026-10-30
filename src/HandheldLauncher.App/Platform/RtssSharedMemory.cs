using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace HandheldLauncher.App.Platform;

/// <summary>FPS/frametime que o RTSS mediu pra um processo.</summary>
internal readonly record struct FrameStats(double Fps, double FrameTimeMs, int ProcessId);

/// <summary>
/// Conversa com o RTSS (RivaTuner Statistics Server) pela memória
/// compartilhada "RTSSSharedMemoryV2" — o mesmo canal que MSI Afterburner,
/// HWiNFO e CapFrameX usam:
/// <list type="bullet">
/// <item><b>Lê</b> o FPS/frametime que o RTSS mede de dentro do jogo (a parte
/// difícil, hook de DirectX/Vulkan — não reinventamos).</item>
/// <item><b>Escreve</b> o texto do nosso HUD num "slot" de OSD próprio
/// (dono "HandheldLauncher"). O RTSS desenha esse texto DENTRO do frame do
/// jogo — funciona até em tela cheia exclusiva, onde uma janela nossa por
/// cima não apareceria. Cores via tags do RTSS (&lt;C=RRGGBB&gt;).</item>
/// </list>
/// Layout segundo o RTSSSharedMemory.h do SDK do RTSS (cabeçalho, entradas
/// de app e de OSD). Nunca testado contra um RTSS de verdade por quem
/// escreveu — tudo com checagem de limites e melhor esforço.
/// </summary>
  [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal sealed unsafe class RtssSharedMemory : IDisposable
{
    private const string MapName = "RTSSSharedMemoryV2";
    private const uint SignatureRtss = 0x52545353; // 'RTSS'
    private const string OwnerName = "HandheldLauncher";

    // Cabeçalho (RTSS_SHARED_MEMORY)
    private const int OffVersion = 4;
    private const int OffAppEntrySize = 8;
    private const int OffAppArrOffset = 12;
    private const int OffAppArrSize = 16;
    private const int OffOsdEntrySize = 20;
    private const int OffOsdArrOffset = 24;
    private const int OffOsdArrSize = 28;
    private const int OffOsdFrame = 32;
    private const int OffBusy = 36; // v2.12+

    // Entrada de app (RTSS_SHARED_MEMORY_APP_ENTRY)
    private const int AppOffTime0 = 268;
    private const int AppOffTime1 = 272;
    private const int AppOffFrames = 276;
    private const int AppOffFrameTime = 280; // microssegundos
    private const int AppMinSize = 284;

    // Entrada de OSD (RTSS_SHARED_MEMORY_OSD_ENTRY)
    private const int OsdOffText = 0;       // szOSD[256]
    private const int OsdOffOwner = 256;    // szOSDOwner[256]
    private const int OsdOffTextEx = 512;   // szOSDEx[4096] (v2.7+)
    private const int OsdTextSize = 256;
    private const int OsdTextExSize = 4096;

    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private byte* _base;
    private long _capacity;

    /// <summary>Último problema encontrado (pra mostrar no menu/Configurações), ou null.</summary>
    public string? LastError { get; private set; }

    public bool IsAvailable => EnsureOpen();

    private uint U32(long offset) => *(uint*)(_base + offset);

    private bool EnsureOpen()
    {
        if (_base != null)
        {
            // RTSS marca a assinatura como 'DEAD' ao fechar.
            if (U32(0) == SignatureRtss) return true;
            Close();
        }

        try
        {
            _file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.ReadWrite);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            _base = pointer + _view.PointerOffset;
            _capacity = _view.Capacity;

            if (_capacity < 64 || U32(0) != SignatureRtss)
            {
                LastError = "RTSS ainda iniciando";
                Close();
                return false;
            }

            LastError = null;
            return true;
        }
        catch (FileNotFoundException)
        {
            LastError = "RTSS não está rodando";
        }
        catch (UnauthorizedAccessException)
        {
            LastError = "sem permissão pra acessar o RTSS";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        Close();
        return false;
    }

    /// <summary>
    /// FPS do processo <paramref name="preferredPid"/> (normalmente o da janela
    /// em primeiro plano) se o RTSS estiver medindo ele; senão, o app medido
    /// mais recentemente. Null se nada está sendo medido agora.
    /// </summary>
    public FrameStats? ReadFrameStats(int preferredPid)
    {
        if (!EnsureOpen()) return null;

        try
        {
            var entrySize = U32(OffAppEntrySize);
            var arrOffset = U32(OffAppArrOffset);
            var arrSize = U32(OffAppArrSize);
            if (entrySize < AppMinSize) return null;

            var now = (uint)Environment.TickCount;
            var ownPid = Environment.ProcessId;
            FrameStats? best = null;
            uint bestTime = 0;

            for (uint i = 0; i < arrSize; i++)
            {
                long entry = arrOffset + (long)i * entrySize;
                if (entry + AppMinSize > _capacity) break;

                var pid = (int)U32(entry);
                if (pid == 0 || pid == ownPid) continue;

                var t0 = U32(entry + AppOffTime0);
                var t1 = U32(entry + AppOffTime1);
                var frames = U32(entry + AppOffFrames);
                if (t1 <= t0 || frames == 0) continue;
                if (now - t1 > 3000) continue; // parado (jogo fechado/minimizado)

                var fps = 1000.0 * frames / (t1 - t0);
                var frameTimeMs = U32(entry + AppOffFrameTime) / 1000.0;
                if (frameTimeMs <= 0 && fps > 0) frameTimeMs = 1000.0 / fps;

                var stats = new FrameStats(fps, frameTimeMs, pid);
                if (pid == preferredPid) return stats;
                if (t1 > bestTime)
                {
                    bestTime = t1;
                    best = stats;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Escreve (ou atualiza) o texto do nosso slot de OSD. False se o RTSS não está disponível.</summary>
    public bool WriteOsd(string text) => WithOsdSlot(claim: true, entry =>
    {
        var version = U32(OffVersion);
        if (version >= 0x00020007)
        {
            WriteAnsi(entry + OsdOffTextEx, OsdTextExSize, text);
            WriteAnsi(entry + OsdOffText, OsdTextSize, string.Empty);
        }
        else
        {
            WriteAnsi(entry + OsdOffText, OsdTextSize, text);
        }
    });

    /// <summary>Apaga nosso texto e libera o slot (HUD desligado, launcher fechando/atualizando).</summary>
    public void ClearOsd()
    {
        WithOsdSlot(claim: false, entry =>
        {
            var version = U32(OffVersion);
            var length = version >= 0x00020007 ? OsdOffTextEx + OsdTextExSize : OsdOffOwner + 256;
            new Span<byte>(_base + entry, length).Clear();
        });
    }

    private bool WithOsdSlot(bool claim, Action<long> action)
    {
        if (!EnsureOpen()) return false;

        var version = U32(OffVersion);
        if (version < 0x00020000)
        {
            LastError = "versão do RTSS antiga demais";
            return false;
        }

        var usesBusyLock = version >= 0x0002000C;
        var busy = (int*)(_base + OffBusy);
        if (usesBusyLock && !TryLock(busy)) return false;

        try
        {
            var entrySize = U32(OffOsdEntrySize);
            var arrOffset = U32(OffOsdArrOffset);
            var arrSize = U32(OffOsdArrSize);
            if (entrySize < OsdOffOwner + 256) return false;

            // Slot 0 é reservado pro cliente "principal" (Afterburner etc.);
            // terceiros começam do 1 — mesma regra do exemplo do SDK.
            // 1ª passada: slot que já é nosso. 2ª: primeiro slot livre.
            for (var pass = 0; pass < (claim ? 2 : 1); pass++)
            {
                for (uint i = 1; i < arrSize; i++)
                {
                    long entry = arrOffset + (long)i * entrySize;
                    if (entry + Math.Min(entrySize, (uint)(OsdOffTextEx + OsdTextExSize)) > _capacity) break;

                    var owner = Marshal.PtrToStringAnsi((IntPtr)(_base + entry + OsdOffOwner)) ?? string.Empty;
                    var isOurs = owner == OwnerName;
                    var isFree = owner.Length == 0;

                    if ((pass == 0 && isOurs) || (pass == 1 && isFree))
                    {
                        if (pass == 1) WriteAnsi(entry + OsdOffOwner, 256, OwnerName);
                        action(entry);
                        Interlocked.Increment(ref *(int*)(_base + OffOsdFrame));
                        return true;
                    }
                }
            }

            if (claim) LastError = "nenhum slot de OSD livre no RTSS";
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
        finally
        {
            if (usesBusyLock) Interlocked.Exchange(ref *busy, 0);
        }
    }

    private static bool TryLock(int* busy)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (Interlocked.CompareExchange(ref *busy, 1, 0) == 0) return true;
            Thread.SpinWait(64);
        }
        return false;
    }

    private void WriteAnsi(long offset, int capacity, string text)
    {
        // RTSS lê texto ANSI. Latin-1 cobre os acentos do português; o HUD
        // usa rótulos curtos em inglês (GPU/CPU/RAM...) igual o MangoHud.
        var bytes = Encoding.Latin1.GetBytes(text);
        var length = Math.Min(bytes.Length, capacity - 1);
        var target = new Span<byte>(_base + offset, capacity);
        bytes.AsSpan(0, length).CopyTo(target);
        target[length] = 0;
    }

    private void Close()
    {
        try
        {
            if (_base != null && _view is not null) _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
        catch
        {
            // Melhor esforço.
        }

        _base = null;
        _capacity = 0;
        _view?.Dispose();
        _view = null;
        _file?.Dispose();
        _file = null;
    }

    public void Dispose() => Close();
}
