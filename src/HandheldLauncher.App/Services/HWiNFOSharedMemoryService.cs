using System.IO.MemoryMappedFiles;
using System.Text;

namespace HandheldLauncher.App.Services;

/// <summary>
/// Leitor opcional da interface HWiNFO Shared Memory (SM2).
/// Não instala nem inicia HWiNFO; apenas consome a área de memória publicada por ele.
/// </summary>
public sealed class HWiNFOSharedMemoryService : IDisposable
{
    private const string MappingName = @"Global\HWiNFO_SENS_SM2";
    private const int HeaderMinimumSize = 44;
    private const int SensorMinimumSize = 264;
    private const int ReadingMinimumSize = 316;
    private const int MaxElements = 100_000;

    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;

    public sealed record Reading(string SensorName, string Name, string Unit, double Value, double Min, double Max, double Average, uint Type);
    public sealed record Snapshot(DateTime PollTime, uint PollingPeriodMs, IReadOnlyList<Reading> Readings);

    public bool TryRead(out Snapshot? snapshot)
    {
        snapshot = null;
        try
        {
            EnsureMapping();
            if (_view is null) return false;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var before = ReadHeader();
                if (!before.Valid || before.ReadingCount == 0) return false;

                var readings = ReadRows(before);
                var after = ReadHeader();

                // O HWiNFO repovoa a tabela a cada polling. Só aceitamos o snapshot
                // se o cabeçalho permaneceu estável durante a cópia.
                if (before.PollTime == after.PollTime &&
                    before.ReadingCount == after.ReadingCount &&
                    before.ReadingOffset == after.ReadingOffset &&
                    before.ReadingSize == after.ReadingSize)
                {
                    snapshot = new Snapshot(
                        DateTimeOffset.FromUnixTimeSeconds(before.PollTime).LocalDateTime,
                        before.PollingPeriodMs,
                        readings);
                    return true;
                }
                Thread.Sleep(15);
            }
        }
        catch (FileNotFoundException) { ResetMapping(); }
        catch (ObjectDisposedException) { ResetMapping(); }
        catch (UnauthorizedAccessException) { ResetMapping(); }
        catch (IOException) { ResetMapping(); }
        catch (ArgumentException) { ResetMapping(); }

        return false;
    }

    private void EnsureMapping()
    {
        if (_view is not null) return;
        _mapping = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
        _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    private readonly record struct Header(
        bool Valid, long PollTime, uint SensorOffset, uint SensorSize, uint SensorCount,
        uint ReadingOffset, uint ReadingSize, uint ReadingCount, uint PollingPeriodMs);

    private Header ReadHeader()
    {
        var view = _view ?? throw new ObjectDisposedException(nameof(HWiNFOSharedMemoryService));
        if (view.Capacity < HeaderMinimumSize) return default;

        var signature = new byte[4];
        view.ReadArray(0, signature, 0, signature.Length);
        if (Encoding.ASCII.GetString(signature) != "HWiS") return default;

        var version = view.ReadUInt32(4);
        var revision = view.ReadUInt32(8);
        var pollTime = view.ReadInt64(12);
        var sensorOffset = view.ReadUInt32(20);
        var sensorSize = view.ReadUInt32(24);
        var sensorCount = view.ReadUInt32(28);
        var readingOffset = view.ReadUInt32(32);
        var readingSize = view.ReadUInt32(36);
        var readingCount = view.ReadUInt32(40);
        var pollingPeriod = revision >= 1 && view.Capacity >= 48 ? view.ReadUInt32(44) : 0;

        var valid = version >= 1 &&
                    sensorCount <= MaxElements && readingCount <= MaxElements &&
                    (sensorCount == 0 || sensorSize >= SensorMinimumSize) &&
                    (readingCount == 0 || readingSize >= ReadingMinimumSize) &&
                    IsRangeValid(sensorOffset, sensorSize, sensorCount, view.Capacity) &&
                    IsRangeValid(readingOffset, readingSize, readingCount, view.Capacity);

        return new Header(valid, pollTime, sensorOffset, sensorSize, sensorCount,
            readingOffset, readingSize, readingCount, pollingPeriod);
    }

    private static bool IsRangeValid(uint offset, uint elementSize, uint count, long capacity)
    {
        if (count == 0) return true;
        var end = (ulong)offset + (ulong)elementSize * count;
        return offset > 0 && end <= (ulong)capacity;
    }

    private IReadOnlyList<Reading> ReadRows(Header header)
    {
        var view = _view ?? throw new ObjectDisposedException(nameof(HWiNFOSharedMemoryService));
        var sensorNames = new string[header.SensorCount];
        var sensorBuffer = new byte[SensorMinimumSize];

        for (var i = 0; i < sensorNames.Length; i++)
        {
            var offset = (long)header.SensorOffset + (long)header.SensorSize * i;
            view.ReadArray(offset, sensorBuffer, 0, sensorBuffer.Length);
            sensorNames[i] = ReadText(sensorBuffer, 8, 128);
            if (string.IsNullOrWhiteSpace(sensorNames[i]))
                sensorNames[i] = ReadText(sensorBuffer, 136, 128);
        }

        var result = new List<Reading>((int)header.ReadingCount);
        var row = new byte[ReadingMinimumSize];
        for (var i = 0; i < header.ReadingCount; i++)
        {
            var offset = (long)header.ReadingOffset + (long)header.ReadingSize * i;
            view.ReadArray(offset, row, 0, row.Length);

            var type = BitConverter.ToUInt32(row, 0);
            var sensorIndex = BitConverter.ToUInt32(row, 4);
            var name = ReadText(row, 140, 128);
            if (string.IsNullOrWhiteSpace(name)) name = ReadText(row, 12, 128);
            var unit = ReadText(row, 268, 16);
            var value = BitConverter.ToDouble(row, 284);
            var min = BitConverter.ToDouble(row, 292);
            var max = BitConverter.ToDouble(row, 300);
            var average = BitConverter.ToDouble(row, 308);
            var sensorName = sensorIndex < sensorNames.Length ? sensorNames[sensorIndex] : string.Empty;

            if (double.IsFinite(value))
                result.Add(new Reading(sensorName, name, unit, value, min, max, average, type));
        }

        return result;
    }

    private static string ReadText(byte[] buffer, int offset, int length)
    {
        var count = 0;
        while (count < length && buffer[offset + count] != 0) count++;
        return Encoding.UTF8.GetString(buffer, offset, count).Trim();
    }

    private void ResetMapping()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
    }

    public void Dispose() => ResetMapping();
}
