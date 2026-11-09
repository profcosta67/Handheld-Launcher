using System.Text.Json;
using HandheldLauncher.Core.Models;
using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// v0.24.1: primeira cobertura do settings.json. O risco concreto que isto
/// trava: acrescentar uma definição nova (aqui o HidHideElevateOnDemand) e ela
/// não persistir — ou um settings.json de uma versão anterior (sem a chave)
/// passar a rebentar/ligar sozinho o que devia estar desligado.
/// </summary>
public class AppSettingsStoreTests
{
    private static string TempSettingsPath()
        => Path.Combine(Path.GetTempPath(), "HandheldLauncherTests", Guid.NewGuid().ToString("N"), "settings.json");

    private static void CleanUp(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // melhor esforço: é uma pasta temporária de teste
        }
    }

    [Fact]
    public void Defaults_AreSafe()
    {
        var settings = new AppSettings();

        Assert.True(settings.IsolateControllerOnOverlay);
        Assert.False(settings.HidHideElevateOnDemand); // nada de UAC automático
        Assert.False(settings.FreezeGameOnOverlay);
        Assert.False(settings.BetaChannelOptIn);
        Assert.True(settings.KioskMode);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsHidHideFlags()
    {
        var path = TempSettingsPath();
        try
        {
            var saved = new AppSettings
            {
                IsolateControllerOnOverlay = false,
                HidHideElevateOnDemand = true,
                Theme = 3,
                TileSize = 2,
            };

            AppSettingsStore.Save(saved, path);
            var loaded = AppSettingsStore.Load(path);

            Assert.False(loaded.IsolateControllerOnOverlay);
            Assert.True(loaded.HidHideElevateOnDemand);
            Assert.Equal(3, loaded.Theme);
            Assert.Equal(2, loaded.TileSize);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsLists()
    {
        var path = TempSettingsPath();
        try
        {
            var saved = new AppSettings
            {
                XboxScanFolders = ["D:\\Xbox", "E:\\Jogos"],
                EdenRomFolders = ["F:\\Roms"],
            };

            AppSettingsStore.Save(saved, path);
            var loaded = AppSettingsStore.Load(path);

            Assert.Equal(saved.XboxScanFolders, loaded.XboxScanFolders);
            Assert.Equal(saved.EdenRomFolders, loaded.EdenRomFolders);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Load_LegacyJsonWithoutNewFlag_KeepsItOff()
    {
        var path = TempSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Como estava o settings.json de quem já tinha a 0.24.0: sem a chave nova.
            File.WriteAllText(path, "{ \"IsolateControllerOnOverlay\": true, \"Theme\": 5 }");

            var loaded = AppSettingsStore.Load(path);

            Assert.True(loaded.IsolateControllerOnOverlay);
            Assert.False(loaded.HidHideElevateOnDemand);
            Assert.Equal(5, loaded.Theme);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Load_MissingFile_CreatesDefaultsOnDisk()
    {
        var path = TempSettingsPath();
        try
        {
            var loaded = AppSettingsStore.Load(path);

            Assert.True(File.Exists(path));
            Assert.True(loaded.IsolateControllerOnOverlay);
            Assert.False(loaded.HidHideElevateOnDemand);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaultsInsteadOfThrowing()
    {
        var path = TempSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "isto não é json {{{");

            var loaded = AppSettingsStore.Load(path);

            Assert.NotNull(loaded);
            Assert.True(loaded.IsolateControllerOnOverlay);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Save_DoesNotWriteSecretsInPlainText()
    {
        var path = TempSettingsPath();
        try
        {
            var saved = new AppSettings
            {
                SteamWebApiKey = "segredo-de-teste",
                SteamGridDbApiKey = "outro-segredo",
                RawgApiKey = "mais-um",
            };

            AppSettingsStore.Save(saved, path);
            var json = File.ReadAllText(path);

            if (OperatingSystem.IsWindows())
            {
                // Windows: DPAPI. O valor em claro nunca pode aparecer no arquivo.
                Assert.DoesNotContain("segredo-de-teste", json);
                Assert.DoesNotContain("outro-segredo", json);
                Assert.Contains("dpapi:", json);
            }

            // Em qualquer plataforma o JSON tem de continuar válido.
            using var _ = JsonDocument.Parse(json);
        }
        finally
        {
            CleanUp(path);
        }
    }
}
