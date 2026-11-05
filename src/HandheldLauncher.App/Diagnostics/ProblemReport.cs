using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HandheldLauncher.App.Platform;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Diagnostics;

/// <summary>
/// v0.16.2 — Central de Ajuda, etapa 3: "Reportar erro".
///
/// Monta um .zip na Área de Trabalho com tudo que ajuda a investigar um
/// problema — e SEM dados pessoais, porque o repositório é público e o
/// pacote pode ir pra um grupo ou pro GitHub:
/// <list type="bullet">
/// <item>relatorio.txt: versão, canal, Windows, modelo do aparelho, CPU,
/// resultado das verificações da Central e da restauração da sessão.</item>
/// <item>debug.log / debug.old.log: cópias "limpas".</item>
/// <item>configuracoes.json: as configurações sem as chaves de API e sem o
/// SteamID.</item>
/// </list>
/// Em tudo, trocamos: chaves de API → &lt;chave&gt;, SteamID → &lt;steamid&gt;,
/// pasta do usuário → %USERPROFILE%, nome de usuário → &lt;usuario&gt;, nome do
/// PC → &lt;pc&gt;. Os originais no aparelho não são alterados.
///
/// Também monta o link "New issue" do GitHub já preenchido (versão, aparelho,
/// verificações com problema) — a pessoa só descreve o problema e arrasta o
/// .zip. Quem não tem conta no GitHub pode mandar o .zip por onde quiser.
/// </summary>
internal static class ProblemReport
{
    public sealed record Created(string ZipPath, string IssueUrl);

    private const string IssuesNewUrl = UpdateService.RepoUrl + "/issues/new";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandheldLauncher");

    public static Task<Created> CreateAsync(IReadOnlyList<HealthChecks.Result> checks) => Task.Run(() =>
    {
        var redactor = Redactor.FromCurrentUser();
        var now = DateTime.Now;
        var device = DescribeDevice();
        var version = CurrentVersion();
        var channel = UpdateService.IsBetaChannel ? "beta" : "estável";

        var report = BuildReportText(now, version, channel, device, checks);

        var zipPath = Path.Combine(PickOutputFolder(), $"HandheldLauncher-relatorio-{now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddText(zip, "relatorio.txt", redactor.Apply(report));
            foreach (var name in new[] { "debug.log", "debug.old.log" })
            {
                var content = ReadShared(Path.Combine(DataDir, name));
                if (content is not null) AddText(zip, name, redactor.Apply(content));
            }
            // v0.16.4: os 3 relatórios de crash mais recentes, se houver.
            foreach (var crash in CrashReport.Latest(3))
            {
                var content = ReadShared(crash);
                if (content is not null) AddText(zip, "crashes/" + Path.GetFileName(crash), redactor.Apply(content));
            }
            var settings = SanitizedSettingsJson();
            if (settings is not null) AddText(zip, "configuracoes.json", redactor.Apply(settings));
        }

        DebugLog.Write($"[AJUDA] Pacote de relatório criado: {redactor.Apply(zipPath)}");
        var issueUrl = BuildIssueUrl(version, channel, device, checks, Path.GetFileName(zipPath));
        return new Created(zipPath, issueUrl);
    });

    public static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AJUDA] Não consegui mostrar o pacote no Explorer: {ex.Message}");
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AJUDA] Não consegui abrir o navegador: {ex.Message}");
        }
    }

    // --------------------------------------------------------------------

    private static string BuildReportText(DateTime now, string version, string channel, string device, IReadOnlyList<HealthChecks.Result> checks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Handheld Launcher — relatório de problema");
        sb.AppendLine($"Gerado em: {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Versão: {version} (canal {channel})");
        sb.AppendLine($"Windows: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"Aparelho: {device}");
        sb.AppendLine();

        sb.AppendLine("== Verificações da Central de Ajuda ==");
        foreach (var check in checks.OrderByDescending(c => c.Status))
        {
            sb.AppendLine($"{StatusText(check.Status)} {check.Title}: {check.Summary}");
            if (!string.IsNullOrWhiteSpace(check.Hint)) sb.AppendLine($"    dica: {check.Hint}");
        }
        sb.AppendLine();

        sb.AppendLine("== Restauração da última sessão ==");
        var restore = StartupRestoreStatus.Snapshot();
        if (restore.Count == 0) sb.AppendLine("(nada registrado)");
        foreach (var entry in restore.OrderBy(e => e.Item))
            sb.AppendLine($"{(entry.Ok ? "[ok]" : "[FALHOU]")} {entry.Item}: {entry.Detail} ({entry.AtLocal:HH:mm:ss})");
        sb.AppendLine();

        if (OperatingSystem.IsWindows())
        {
            sb.AppendLine("== Controle (no momento do relatório) ==");
            sb.Append(ControllerDiagnostics.DescribeNow());
            sb.AppendLine();
        }

        sb.AppendLine("== Atualizações ==");
        var last = UpdateService.LastCheck;
        sb.AppendLine(last is null ? "(nenhuma verificação ainda)" : $"{last.AtLocal:HH:mm:ss}: {last.Result}");

        return sb.ToString();
    }

    private static string BuildIssueUrl(string version, string channel, string device, IReadOnlyList<HealthChecks.Result> checks, string zipName)
    {
        var flagged = checks
            .Where(c => c.Status is HealthChecks.Status.Error or HealthChecks.Status.Warning)
            .OrderByDescending(c => c.Status)
            .Select(c => $"- {HealthChecks.Icon(c.Status)} {c.Title}: {c.Summary}")
            .ToList();

        var body = new StringBuilder();
        body.AppendLine("## O que aconteceu");
        body.AppendLine("(descreva o problema)");
        body.AppendLine();
        body.AppendLine("## Quando aconteceu (horário aproximado)");
        body.AppendLine();
        body.AppendLine("## O que você estava fazendo");
        body.AppendLine();
        body.AppendLine("---");
        body.AppendLine($"**Versão:** {version} (canal {channel})");
        body.AppendLine($"**Aparelho:** {device}");
        body.AppendLine("**Verificações com atenção/problema:**");
        if (flagged.Count == 0) body.AppendLine("- nenhuma");
        foreach (var line in flagged) body.AppendLine(Truncate(line, 300));
        body.AppendLine();
        body.AppendLine($"📎 Arraste aqui o arquivo **{zipName}** (está na sua Área de Trabalho).");

        var redactor = Redactor.FromCurrentUser();
        var url = $"{IssuesNewUrl}?labels=bug&title={Uri.EscapeDataString("[Bug] ")}&body={Uri.EscapeDataString(redactor.Apply(body.ToString()))}";
        // Navegadores e o GitHub aceitam URLs bem longas, mas não infinitas.
        return url.Length <= 7000 ? url : $"{IssuesNewUrl}?labels=bug&title={Uri.EscapeDataString("[Bug] ")}";
    }

    private static string? SanitizedSettingsJson()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            settings.SteamGridDbApiKey = Mask(settings.SteamGridDbApiKey);
            settings.SteamWebApiKey = Mask(settings.SteamWebApiKey);
            settings.RawgApiKey = Mask(settings.RawgApiKey);
            settings.SteamId64 = Mask(settings.SteamId64);
            return JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[AJUDA] Não consegui incluir as configurações no pacote: {ex.Message}");
            return null;
        }

        // Só diz SE estava preenchido — nunca o valor.
        static string Mask(string? value) => string.IsNullOrWhiteSpace(value) ? "" : "<preenchido — removido do relatório>";
    }

    private static string DescribeDevice()
    {
        if (!OperatingSystem.IsWindows()) return Environment.OSVersion.ToString();
        try
        {
            using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            using var cpu = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var maker = bios?.GetValue("SystemManufacturer")?.ToString()?.Trim();
            var model = bios?.GetValue("SystemProductName")?.ToString()?.Trim();
            var family = bios?.GetValue("SystemFamily")?.ToString()?.Trim();
            var cpuName = cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim();
            var parts = new[] { maker, family, model }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct();
            var name = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(cpuName) ? name : $"{name} · {cpuName}";
        }
        catch
        {
            return "(não identificado)";
        }
    }

    private static string PickOutputFolder()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop)) return desktop;
        }
        catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
        Directory.CreateDirectory(DataDir);
        return DataDir;
    }

    /// <summary>Lê mesmo com o launcher escrevendo no arquivo ao mesmo tempo.</summary>
    private static string? ReadShared(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"(não foi possível ler {Path.GetFileName(path)}: {ex.Message})";
        }
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string StatusText(HealthChecks.Status status) => status switch
    {
        HealthChecks.Status.Ok => "[ok]",
        HealthChecks.Status.Info => "[info]",
        HealthChecks.Status.Warning => "[atenção]",
        _ => "[problema]",
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string CurrentVersion()
    {
        try
        {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "dev";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
        catch
        {
            return "dev";
        }
    }

    /// <summary>Remove dados pessoais de um texto (ver resumo da classe).</summary>
    internal sealed class Redactor
    {
        private static readonly Regex SteamIdPattern = new(@"7656119\d{10}", RegexOptions.Compiled);

        // v0.16.3: controles Bluetooth aparecem com o endereço do aparelho no
        // nome do dispositivo HID ("...&REV_0523&c0d6d5e1ea89&IG_00").
        private static readonly Regex BluetoothAddressPattern = new(@"(?<=&)[0-9a-fA-F]{12}(?=&)", RegexOptions.Compiled);

        private readonly List<string> _secrets;
        private readonly string? _profile;
        private readonly string? _user;
        private readonly string? _machine;

        private Redactor(List<string> secrets, string? profile, string? user, string? machine)
        {
            _secrets = secrets;
            _profile = profile;
            _user = user;
            _machine = machine;
        }

        public static Redactor FromCurrentUser()
        {
            var secrets = new List<string>();
            try
            {
                var s = AppSettingsStore.Load();
                foreach (var value in new[] { s.SteamGridDbApiKey, s.SteamWebApiKey, s.RawgApiKey, s.SteamId64 })
                    if (!string.IsNullOrWhiteSpace(value) && value.Length >= 6) secrets.Add(value);
            }
            catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }

            string? profile = null, user = null, machine = null;
            try { profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            try { user = Environment.UserName; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            try { machine = Environment.MachineName; } catch (Exception swallowedEx) { HandheldLauncher.App.Diagnostics.DebugLog.Swallowed(swallowedEx); }
            return new Redactor(secrets, profile, user, machine);
        }

        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            foreach (var secret in _secrets)
                text = text.Replace(secret, "<chave>", StringComparison.Ordinal);

            text = SteamIdPattern.Replace(text, "<steamid>");
            text = BluetoothAddressPattern.Replace(text, "<bt>");

            if (!string.IsNullOrWhiteSpace(_profile) && _profile.Length >= 4)
                text = text.Replace(_profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(_user) && _user.Length >= 3)
                text = Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(_user)}(?![\p{{L}}\p{{N}}])", "<usuario>", RegexOptions.IgnoreCase);

            if (!string.IsNullOrWhiteSpace(_machine) && _machine.Length >= 3)
                text = Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(_machine)}(?![\p{{L}}\p{{N}}])", "<pc>", RegexOptions.IgnoreCase);

            return text;
        }
    }
}
