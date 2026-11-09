namespace HandheldLauncher.Core.Services;

/// <summary>Um dispositivo PnP como o Windows o reporta (o que o HidHide precisa).</summary>
public sealed record HidDeviceInfo(string DeviceId, string? Name = null, string? PnpClass = null);

/// <summary>
/// Escolhe, na lista de dispositivos do Windows, quais têm de ser escondidos do
/// jogo enquanto o Quick Settings está aberto.
///
/// <para>v0.24.1: isto estava dentro do <c>HidHideService</c> a comparar com
/// <c>deviceId.Contains("MI_00&amp;Col01")</c> — e o Windows reporta
/// <c>MI_00&amp;COL01</c> (maiúsculas). <c>Contains</c> é ordinal, portanto a
/// procura nunca encontrava o controle do Legion.</para>
///
/// <para>v0.24.2: além disso, só o controle da Lenovo era escondido. Com um
/// <b>Xbox</b> emparelhado (BT/BLE) o jogo continuava a receber os comandos —
/// prova no debug.log de 09/11: o launcher registava
/// <c>[HIDHIDE] Controle OCULTADO … FarCry6.exe</c> enquanto o slot 0 era
/// <c>VID_045e&amp;PID_0b13…IG_00</c>. Agora entram todos os controles
/// detetados, com o controle embutido do Legion primeiro.</para>
/// </summary>
public static class HidHideDeviceSelector
{
    /// <summary>Vendor ID do controle embutido nos Legion Go / Legion Go S.</summary>
    public const string LegionVendorId = "VID_17EF";

    /// <summary>Marcadores do dispositivo Lenovo, do mais específico para o mais genérico.</summary>
    private static readonly string[] LegionMarkers =
    [
        "MI_00&COL01",
        "IG_00",
        "IG_01",
    ];

    /// <summary>
    /// Fabricantes conhecidos de controles — o "e afins". Isto é a rede de
    /// segurança para Windows noutro idioma, onde os nomes localizados não
    /// casam: ROG Ally (ASUS), Steam Deck (Valve), DualSense/DualShock (Sony),
    /// Xbox (Microsoft), Nintendo, 8BitDo, Razer, Hori, PowerA…
    ///
    /// <para>Não substitui as outras regras (VID do Legion, nomes e
    /// <c>IG_00</c>): soma-se a elas. E passa sempre DEPOIS da lista de
    /// exclusão — teclados e ratos destes mesmos fabricantes continuam de fora.</para>
    /// </summary>
    private static readonly string[] GamepadVendorIds =
    [
        "VID_0B05", // ASUS (ROG Ally)
        "VID_28DE", // Valve (Steam Controller / Deck)
        "VID_054C", // Sony (DualSense / DualShock)
        "VID_045E", // Microsoft (Xbox)
        "VID_057E", // Nintendo
        "VID_2DC8", // 8BitDo
        "VID_1532", // Razer
        "VID_0F0D", // Hori
        "VID_20D6", // PowerA
        "VID_2E24", // Hyperkin
    ];

    /// <summary>
    /// Nomes (localizados) que identificam um controle. O Windows 11 chama ao
    /// Xbox BLE "…Compatível com Bluetooth LE XINPUT" e ao USB "…Compatível com
    /// XINPUT" — "XINPUT" não é traduzido.
    /// </summary>
    private static readonly string[] ControllerNameMarkers =
    [
        "XINPUT",
        "GAME CONTROLLER",
        "GAMEPAD",
        "CONTROLADOR DE JOGO",
        "CONTROLADOR DE JUEGOS",
        "WIRELESS CONTROLLER",
        "DUALSENSE",
        "DUALSHOCK",
    ];

    /// <summary>
    /// Nomes que NUNCA podem entrar na blocklist: esconder teclado/rato/touch do
    /// jogo seria pior do que o problema que isto resolve.
    /// </summary>
    private static readonly string[] NeverBlockMarkers =
    [
        "TECLADO",
        "KEYBOARD",
        "MOUSE",
        "RATO",
        "TOUCH",
        "TOQUE",
        "INPUT CONFIGURATION",
        "CONFIGURAÇÃO DA MICROSOFT",
        "CONFIGURACION DE MICROSOFT",
    ];

    /// <summary>
    /// Todos os dispositivos a bloquear, por ordem de prioridade: coleção de
    /// jogo do Legion (MI_00), interface XInput do Legion, e depois qualquer
    /// outro controle detetado. Sem duplicados.
    /// </summary>
    public static IReadOnlyList<string> SelectControllerIds(IEnumerable<string?> deviceIds)
        => SelectControllerIds((deviceIds ?? []).Select(id => new HidDeviceInfo(id ?? string.Empty)));

    /// <summary>Versão com nomes — é o que permite reconhecer controles de outras marcas.</summary>
    public static IReadOnlyList<string> SelectControllerIds(IEnumerable<HidDeviceInfo> devices)
    {
        if (devices is null) return [];

        var rankings = new List<(int Rank, string Id)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in devices)
        {
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId)) continue;
            if (!seen.Add(device.DeviceId)) continue;

            var rank = Classify(device);
            if (rank >= 0) rankings.Add((rank, device.DeviceId));
        }

        return rankings.OrderBy(r => r.Rank).Select(r => r.Id).ToList();
    }

    /// <summary>O primeiro da lista (usado pelos testes da v0.24.1).</summary>
    public static string? SelectControllerId(IEnumerable<string?> deviceIds)
        => SelectControllerIds(deviceIds).FirstOrDefault();

    /// <summary>Ranking do dispositivo, ou -1 quando não é um controle.</summary>
    private static int Classify(HidDeviceInfo device)
    {
        var id = device.DeviceId;
        var name = device.Name ?? string.Empty;

        if (NeverBlockMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return -1;

        // 0..2: coleções do controle embutido do Legion (a de jogo primeiro).
        if (id.Contains(LegionVendorId, StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < LegionMarkers.Length; i++)
            {
                if (id.Contains(LegionMarkers[i], StringComparison.OrdinalIgnoreCase)) return i;
            }
        }

        // 3: fabricante conhecido de controles (ROG Ally, Deck, DualSense…).
        // Só chega aqui quem não é teclado/rato/touch (a exclusão é a primeira).
        if (GamepadVendorIds.Any(vid => id.Contains(vid, StringComparison.OrdinalIgnoreCase)))
            return 3;

        // 3: qualquer dispositivo cujo NOME diga que é um controle (Xbox BT/USB,
        // DualSense, pads de outras marcas). Cobre o BTHLEDEVICE do Xbox, que não
        // é HIDClass mas é o que o jogo usa.
        if (ControllerNameMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return 3;

        // 4: interface XInput "crua" (HID\…&IG_00) sem nome reconhecível.
        if (id.Contains("IG_00", StringComparison.OrdinalIgnoreCase)) return 4;

        return -1;
    }

    /// <summary>
    /// Dispositivos do fornecedor esperado, para o log de diagnóstico quando a
    /// seleção não encontrou nada.
    /// </summary>
    public static IReadOnlyList<string> VendorCandidates(IEnumerable<string?> deviceIds)
        => (deviceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id)
                         && id!.Contains(LegionVendorId, StringComparison.OrdinalIgnoreCase))
            .Select(id => id!)
            .ToList();
}
