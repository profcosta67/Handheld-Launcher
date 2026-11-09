using HandheldLauncher.Core.Services;
using Xunit;

namespace HandheldLauncher.Core.Tests;

/// <summary>
/// v0.24.2: o mascaramento deixou de ser só do controle Lenovo.
///
/// Prova do bug (debug.log de 09/11, Far Cry 6): o launcher registava
/// <c>[HIDHIDE] Controle OCULTADO … FarCry6.exe</c> mas o slot 0 era um
/// **Xbox** por Bluetooth LE (<c>VID_045e&amp;PID_0b13…IG_00</c>) — o jogo
/// continuava a receber os comandos do Quick Settings. Os IDs abaixo são os
/// que o Windows desta máquina reporta.
/// </summary>
public class HidHideMultiDeviceSelectionTests
{
    private const string LegionGamepad = @"HID\VID_17EF&PID_61ED&MI_00&COL01\8&26A8D4DA&0&0000";

    private const string XboxBleId =
        @"BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02045E_PID&0B13_REV&0523_C0D6D5E1EA89\A&2EA8E73D&1&0016";

    private const string XboxUsbId = @"HID\VID_045E&PID_02FF&IG_00\9&2EB6E3AD&0&0000";

    [Fact]
    public void SelectControllerIds_IncludesXboxBleControllerByName()
    {
        var devices = new[]
        {
            new HidDeviceInfo(XboxBleId, "Dispositivo de entrada compatível com Bluetooth LE XINPUT"),
        };

        Assert.Equal([XboxBleId], HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_IncludesXboxUsbControllerByName()
    {
        var devices = new[]
        {
            new HidDeviceInfo(XboxUsbId, "Dispositivo HID compatível com XINPUT"),
        };

        Assert.Equal([XboxUsbId], HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_PutsTheLegionGamepadFirst()
    {
        var devices = new[]
        {
            new HidDeviceInfo(XboxBleId, "Dispositivo de entrada compatível com Bluetooth LE XINPUT"),
            new HidDeviceInfo(LegionGamepad, "Controlador de jogo compatível com HID"),
        };

        var ids = HidHideDeviceSelector.SelectControllerIds(devices);

        Assert.Equal(2, ids.Count);
        Assert.Equal(LegionGamepad, ids[0]);
        Assert.Contains(XboxBleId, ids);
    }

    [Fact]
    public void SelectControllerIds_ReturnsEmptyWhenNoControllerIsAttached()
    {
        var devices = new[]
        {
            new HidDeviceInfo(@"HID\VID_258A&PID_002A&MI_01&COL01\9&7E24852&0&0000", "Controlador de sistema compatível com HID"),
            new HidDeviceInfo(@"HID\VID_1D57&PID_FA60&MI_02&COL01\9&666991&0&0000", "Controlador de sistema compatível com HID"),
        };

        Assert.Empty(HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_NeverBlocksKeyboardMouseOrTouch()
    {
        var devices = new[]
        {
            new HidDeviceInfo(@"HID\VID_17EF&PID_61ED&MI_01&COL03\8&1378B89C&0&0002", "Teclado sensível ao toque compatível com HID"),
            new HidDeviceInfo(@"HID\VID_17EF&PID_61ED&MI_01&COL01\8&1378B89C&0&0000", "Dispositivo de teclado HID"),
            new HidDeviceInfo(@"HID\VID_17EF&PID_61ED&MI_01&COL02\8&1378B89C&0&0001", "Mouse compatível com HID"),
            new HidDeviceInfo(@"HID\VID_17EF&PID_61ED&MI_01&COL04\8&1378B89C&0&0003", "Microsoft Input Configuration Device"),
        };

        Assert.Empty(HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_DoesNotDuplicate()
    {
        var devices = new[]
        {
            new HidDeviceInfo(LegionGamepad, "Controlador de jogo compatível com HID"),
            new HidDeviceInfo(LegionGamepad, "Controlador de jogo compatível com HID"),
        };

        Assert.Equal([LegionGamepad], HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_IgnoresEntriesWithoutId()
    {
        var devices = new[]
        {
            new HidDeviceInfo("", "XINPUT"),
            new HidDeviceInfo("   ", "XINPUT"),
        };

        Assert.Empty(HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerId_StillReturnsTheFirstForCompatibility()
    {
        Assert.Equal(LegionGamepad, HidHideDeviceSelector.SelectControllerId([LegionGamepad]));
        // Sem nome, a interface XInput do Xbox é apanhada pela regra do IG_00.
        Assert.Equal(XboxUsbId, HidHideDeviceSelector.SelectControllerId([XboxUsbId]));
    }

    // --- "e afins": outros aparelhos, mesmo comportamento ---------------------

    [Fact]
    public void SelectControllerIds_IncludesRogAllyControllerEvenWithNonLocalizedName()
    {
        // ASUS: um Windows em espanhol/alemão não traz "controlador de jogo".
        var ally = @"HID\VID_0B05&PID_1ABE&MI_01&COL01\9&2C2B4C5&0&0000";
        var devices = new[] { new HidDeviceInfo(ally, "HID-compliant vendor-defined device") };

        Assert.Equal([ally], HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_IncludesSteamDeckController()
    {
        var deck = @"HID\VID_28DE&PID_1205&MI_02&COL01\7&1E4A2C3&0&0000";
        var devices = new[] { new HidDeviceInfo(deck, "HID-konformes Gerät") };

        Assert.Equal([deck], HidHideDeviceSelector.SelectControllerIds(devices));
    }

    [Fact]
    public void SelectControllerIds_NeverBlocksKeyboardOrMouseFromAGamepadVendor()
    {
        // Microsoft (VID_045E) também faz teclados e ratos: o nome tem prioridade.
        var devices = new[]
        {
            new HidDeviceInfo(@"HID\VID_045E&PID_0823&MI_00\7&2B7F1&0&0000", "Dispositivo de teclado HID"),
            new HidDeviceInfo(@"HID\VID_045E&PID_0823&MI_01\7&2B7F1&0&0001", "Mouse compatível com HID"),
        };

        Assert.Empty(HidHideDeviceSelector.SelectControllerIds(devices));
    }
}
