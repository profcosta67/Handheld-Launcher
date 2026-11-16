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
    public void IgnoresValuesLearnedByOlderRules()
    {
        var path = TempPath();
        try
        {
            // Formato antigo (chave só com o id) e formato por meta sem a marca da regra atual.
            File.WriteAllText(path, "{\"dg\":{\"Watts\":18,\"TargetFps\":30,\"Title\":\"Days Gone\"},\"fc@60\":{\"Watts\":34,\"TargetFps\":60,\"Title\":\"FC\"}}");
            var store = new SmartTdpStore(path);
            Assert.Null(store.Get("dg", 30));
            Assert.Null(store.Get("fc", 60));

            store.Set("fc", "FC", 20, 60);
            Assert.Equal(20, new SmartTdpStore(path).Get("fc", 60)?.Watts);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ClearForgetsOneGameOrAll()
    {
        var path = TempPath();
        try
        {
            var store = new SmartTdpStore(path);
            store.Set("fc", "FC", 20, 60);
            store.Set("fc", "FC", 16, 40);
            store.Set("fc2", "FC 2", 22, 60);
            store.Set("dg", "Days Gone", 18, 30);

            Assert.Equal(2, store.Clear("fc")); // só o "fc", não o "fc2"
            var reread = new SmartTdpStore(path);
            Assert.Null(reread.Get("fc", 60));
            Assert.Null(reread.Get("fc", 40));
            Assert.Equal(22, reread.Get("fc2", 60)?.Watts);

            Assert.Equal(2, reread.Clear(null));
            Assert.Null(new SmartTdpStore(path).Get("dg", 30));
        }
        finally { File.Delete(path); }
    }
}
