using HandheldLauncher.Core.Services.SmartTdp;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class SmartTdpStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"smart-tdp-{Guid.NewGuid():N}.json");

    [Fact]
    public void KeepsOneValuePerTarget()
    {
        var path = TempPath();
        try
        {
            var store = new SmartTdpStore(path);
            store.Set("dg", "Days Gone", 20, 30);
            store.Set("dg", "Days Gone", 28, 40);

            var reread = new SmartTdpStore(path);
            Assert.Equal(20, reread.Get("dg", 30)?.Watts);
            Assert.Equal(28, reread.Get("dg", 40)?.Watts);
            Assert.Null(reread.Get("dg", 60));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadsTheOldFileFormat()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{\"dg\":{\"Watts\":18,\"TargetFps\":30,\"Title\":\"Days Gone\",\"UpdatedUtc\":\"2026-11-14T00:00:00Z\"}}");
            var store = new SmartTdpStore(path);
            Assert.Equal(18, store.Get("dg", 30)?.Watts);
            Assert.Null(store.Get("dg", 60));

            store.Set("dg", "Days Gone", 17, 30);
            Assert.Equal(17, new SmartTdpStore(path).Get("dg", 30)?.Watts);
        }
        finally { File.Delete(path); }
    }
}
