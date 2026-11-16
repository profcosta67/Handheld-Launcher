using HandheldLauncher.Core.Services.SmartTdp;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class SmartTdpTargetStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"smart-tdp-targets-{Guid.NewGuid():N}.json");

    [Fact]
    public void RemembersTheTargetOfEachGame()
    {
        var path = TempPath();
        try
        {
            var store = new SmartTdpTargetStore(path);
            Assert.Null(store.Get("dg"));
            Assert.True(store.Set("dg", "Days Gone", 30));
            Assert.True(store.Set("hades", "Hades", 60));

            var reread = new SmartTdpTargetStore(path);
            Assert.Equal(30, reread.Get("dg"));
            Assert.Equal(60, reread.Get("hades"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NullGoesBackToTheDefaultTarget()
    {
        var path = TempPath();
        try
        {
            var store = new SmartTdpTargetStore(path);
            store.Set("dg", "Days Gone", 40);
            Assert.True(store.Set("dg", "Days Gone", null));
            Assert.Null(store.Get("dg"));
            Assert.Null(new SmartTdpTargetStore(path).Get("dg"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IgnoresBlankIdsAndBrokenFiles()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ isto não é json");
            var store = new SmartTdpTargetStore(path);
            Assert.Null(store.Get("dg"));
            Assert.Null(store.Get(""));
            Assert.False(store.Set("", "x", 30));
            Assert.True(store.Set("dg", "Days Gone", 30));
            Assert.Equal(30, new SmartTdpTargetStore(path).Get("dg"));
            Assert.True(File.Exists(path + ".bad")); // o arquivo estragado foi guardado
        }
        finally { File.Delete(path); File.Delete(path + ".bad"); }
    }
}
