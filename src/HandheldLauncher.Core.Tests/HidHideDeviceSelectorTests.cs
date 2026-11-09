using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// v0.24.1: a deteção do controle deixou de estar presa a maiúsculas/minúsculas
/// do DeviceInstanceId. O caso que falhava no aparelho está no primeiro teste —
/// ID real do Legion Go, copiado do Windows (Win32_PnPEntity).
/// </summary>
public class HidHideDeviceSelectorTests
{
    private const string RealLegionControllerId = @"HID\VID_17EF&PID_61ED&MI_00&COL01\8&26A8D4DA&0&0000";

    [Fact]
    public void SelectControllerId_FindsLegionGamepadCollection()
    {
        Assert.Equal(RealLegionControllerId, HidHideDeviceSelector.SelectControllerId([RealLegionControllerId]));
    }

    [Fact]
    public void SelectControllerId_IsCaseInsensitive()
    {
        var lowerCase = @"hid\vid_17ef&pid_61ed&mi_00&col01\8&26a8d4da&0&0000";
        Assert.Equal(lowerCase, HidHideDeviceSelector.SelectControllerId([lowerCase]));
    }

    [Fact]
    public void SelectControllerId_SelectsXboxXInputInterfaceEvenWithoutName()
    {
        // v0.24.2: esta interface era ignorada (só se mascarava a Lenovo) e o
        // jogo continuava a receber os comandos do Xbox — o bug relatado no
        // teste de 09/11 com o Far Cry 6. Agora é escondida como as outras.
        var xbox = @"HID\VID_045E&PID_02FF&IG_00\9&2EB6E3AD&0&0000";
        Assert.Equal(xbox, HidHideDeviceSelector.SelectControllerId([xbox]));
    }

    [Fact]
    public void SelectControllerId_IgnoresOtherCollectionsOfSameVendor()
    {
        string[] ids =
        [
            @"HID\VID_17EF&PID_61ED&MI_01&COL03\8&1378B89C&0&0002", // teclado
            @"HID\VID_17EF&PID_61ED&MI_01&COL04\8&1378B89C&0&0003", // configuração da Microsoft
            @"HID\VID_17EF&PID_61ED&MI_03\8&28B34A27&0&0000",
        ];

        Assert.Null(HidHideDeviceSelector.SelectControllerId(ids));
    }

    [Fact]
    public void SelectControllerId_PicksTheGamepadFromARealisticMachineList()
    {
        string[] ids =
        [
            @"HID\VID_17EF&PID_61ED&MI_01&COL03\8&1378B89C&0&0002",
            @"HID\VID_045E&PID_02FF&IG_00\9&2EB6E3AD&0&0000",
            RealLegionControllerId,
            @"HID\VID_258A&PID_002A&MI_01&COL01\9&7E24852&0&0000",
        ];

        Assert.Equal(RealLegionControllerId, HidHideDeviceSelector.SelectControllerId(ids));
    }

    [Fact]
    public void SelectControllerId_PrefersMi00Col01OverIg00()
    {
        string[] ids =
        [
            @"HID\VID_17EF&PID_61ED&IG_00\9&2EB6E3AD&0&0000",
            RealLegionControllerId,
        ];

        Assert.Equal(RealLegionControllerId, HidHideDeviceSelector.SelectControllerId(ids));
    }

    [Fact]
    public void SelectControllerId_AcceptsIg00WhenThatIsAllThereIs()
    {
        var ig = @"HID\VID_17EF&PID_61ED&IG_00\9&2EB6E3AD&0&0000";
        Assert.Equal(ig, HidHideDeviceSelector.SelectControllerId([ig]));
    }

    [Fact]
    public void SelectControllerId_ReturnsNullForEmptyOrNullInput()
    {
        Assert.Null(HidHideDeviceSelector.SelectControllerId([]));
        Assert.Null(HidHideDeviceSelector.SelectControllerId([null, "", "   "]));
        Assert.Null(HidHideDeviceSelector.SelectControllerId(null!));
    }

    [Fact]
    public void VendorCandidates_ListsOnlyTheExpectedVendor()
    {
        string?[] ids =
        [
            RealLegionControllerId,
            @"HID\VID_045E&PID_02FF&IG_00\9&2EB6E3AD&0&0000",
            null,
        ];

        var candidates = HidHideDeviceSelector.VendorCandidates(ids);

        Assert.Single(candidates);
        Assert.Equal(RealLegionControllerId, candidates[0]);
    }
}
