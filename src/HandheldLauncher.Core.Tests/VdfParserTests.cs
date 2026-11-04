using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

public class VdfParserTests
{
    [Fact]
    public void Parse_SimpleKeyValue_ReadsStringValue()
    {
        var root = VdfParser.Parse("""
            "AppState"
            {
                "appid"     "123456"
                "name"      "Example Game"
            }
            """);

        var appState = root["AppState"];
        Assert.NotNull(appState);
        Assert.Equal("123456", appState!.GetString("appid"));
        Assert.Equal("Example Game", appState.GetString("name"));
    }

    [Fact]
    public void Parse_NestedObjects_BuildsTree()
    {
        var root = VdfParser.Parse("""
            "libraryfolders"
            {
                "0"
                {
                    "path"  "C:\\SteamLibrary"
                    "apps"
                    {
                        "123"   "0"
                        "456"   "0"
                    }
                }
            }
            """);

        var folder0 = root["libraryfolders"]?["0"];
        Assert.NotNull(folder0);
        Assert.Equal(@"C:\SteamLibrary", folder0!.GetString("path"));

        var apps = folder0["apps"];
        Assert.NotNull(apps);
        Assert.Equal(2, apps!.Children.Count);
        Assert.True(apps.Children.ContainsKey("123"));
    }

    [Fact]
    public void Parse_KeysAreCaseInsensitive()
    {
        var root = VdfParser.Parse("""
            "AppState"
            {
                "Name" "Case Test"
            }
            """);

        // appmanifest_*.acf keys are consistently cased in practice, but the
        // scanner looks them up without worrying about case — this is the
        // contract VdfNode promises (StringComparer.OrdinalIgnoreCase).
        Assert.Equal("Case Test", root["appstate"]?.GetString("name"));
        Assert.Equal("Case Test", root["AppState"]?.GetString("NAME"));
    }

    [Fact]
    public void Parse_EscapedQuotesAndBackslashes_AreUnescaped()
    {
        var root = VdfParser.Parse("""
            "AppState"
            {
                "name" "Say \"Hi\""
                "path" "C:\\Games\\Title"
            }
            """);

        Assert.Equal("Say \"Hi\"", root["AppState"]?.GetString("name"));
        Assert.Equal(@"C:\Games\Title", root["AppState"]?.GetString("path"));
    }

    [Fact]
    public void Parse_LineComment_IsIgnored()
    {
        var root = VdfParser.Parse("""
            "AppState"
            {
                // this line should not break parsing
                "name" "Commented Game"
            }
            """);

        Assert.Equal("Commented Game", root["AppState"]?.GetString("name"));
    }

    [Fact]
    public void Parse_MissingKey_ReturnsNullNotThrow()
    {
        var root = VdfParser.Parse("""
            "AppState"
            {
                "name" "Only Name"
            }
            """);

        Assert.Null(root["AppState"]?.GetString("appid"));
        Assert.Null(root["DoesNotExist"]);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyRoot()
    {
        var root = VdfParser.Parse(string.Empty);

        Assert.True(root.IsLeaf);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void Parse_RealisticAppManifest_ReadsExpectedFields()
    {
        // Formato real de um appmanifest_*.acf do Steam, resumido.
        var root = VdfParser.Parse("""
            "AppState"
            {
                "appid"     "570"
                "Universe"  "1"
                "name"      "Dota 2"
                "StateFlags"        "4"
                "installdir"        "dota 2 beta"
            }
            """);

        var appState = root["AppState"]!;
        Assert.Equal("570", appState.GetString("appid"));
        Assert.Equal("Dota 2", appState.GetString("name"));
        Assert.Equal("dota 2 beta", appState.GetString("installdir"));
    }
}
