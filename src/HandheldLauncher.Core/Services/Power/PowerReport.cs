namespace HandheldLauncher.Core.Services.Power;

/// <summary>Tempo e consumo médio num estado (launcher na frente, jogo, fundo).</summary>
public sealed record StateUsage(string State, double Minutes, double? AvgWatts, double? LauncherCpuPercent);

/// <summary>Uma função do launcher: trabalho por minuto e vezes que acordou por minuto.</summary>
public sealed record ActivityCost(string Name, double MsPerMinute, double WakeupsPerMinute);

/// <summary>Um processo e quanto do processador usou em média, só no tempo na bateria.</summary>
public sealed record ProcessUsage(string Name, double CpuPercent, double CpuPercentWithoutGame, bool IsLauncher);

/// <summary>Quão longe a previsão de tempo restante ficou do ritmo que aconteceu de verdade.</summary>
public sealed record EstimateAccuracy(int Pairs, double? WindowsErrorPercent, double? OurErrorPercent);

public enum FindingLevel { Info, Attention }

public sealed record PowerFinding(FindingLevel Level, string Text);

public sealed class PowerReportResult
{
    public double BatteryMinutes { get; init; }
    public int? PercentDrop { get; init; }
    public double? AvgWatts { get; init; }
    public IReadOnlyList<StateUsage> ByState { get; init; } = Array.Empty<StateUsage>();
    /// <summary>% do processador usado pelo launcher no tempo na bateria (todos os estados).</summary>
    public double? LauncherCpuPercent { get; init; }
    /// <summary>Idem, só sem jogo aberto (launcher na frente ou em segundo plano).</summary>
    public double? LauncherCpuPercentWithoutGame { get; init; }
    public IReadOnlyList<ActivityCost> Activities { get; init; } = Array.Empty<ActivityCost>();
    public IReadOnlyList<ProcessUsage> TopProcesses { get; init; } = Array.Empty<ProcessUsage>();
    public EstimateAccuracy Accuracy { get; init; } = new(0, null, null);
    public IReadOnlyList<PowerFinding> Findings { get; internal set; } = Array.Empty<PowerFinding>();
    public bool HasData => BatteryMinutes >= 1;
}

/// <summary>
/// Conta o relatório do diagnóstico de bateria sobre as amostras do coletor. Lógica pura
/// (testada); a tela só mostra o resultado.
/// </summary>
public static class PowerReport
{
    public const string LauncherProcessName = "HandheldLauncher";

    /// <summary>Uso do launcher sem jogo acima disto (% do processador) vira aviso.</summary>
    public const double LauncherAttentionPercent = 1.0;
    public const double LauncherInfoPercent = 0.3;
    /// <summary>Outro processo acima disto sem jogo aberto vira aviso.</summary>
    public const double ProcessAttentionPercent = 3.0;
    /// <summary>Sem jogo, consumo médio acima disto é alto para um portátil.</summary>
    public const double IdleWattsAttention = 9.0;
    public const int BrightnessInfo = 80;
    public const int RefreshInfoHz = 100;
    public const int IdleTdpInfo = 15;
    /// <summary>Janela usada para conferir a previsão de tempo restante.</summary>
    public static readonly TimeSpan AccuracyMin = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan AccuracyMax = TimeSpan.FromMinutes(40);
    public const int AccuracyMinDropMwh = 200;

    /// <summary>Programas conhecidos por gastar bateria em segundo plano, com a explicação.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownBackground = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HWiNFO64"] = "HWiNFO lendo os sensores o tempo todo (o launcher o abre para mostrar os watts no HUD)",
        ["LegionSpace"] = "Legion Space aberto em segundo plano",
        ["LSDaemon"] = "serviço do Legion Space",
        ["ArmouryCrate"] = "Armoury Crate aberto em segundo plano",
        ["MsMpEng"] = "antivírus do Windows fazendo varredura",
        ["SearchIndexer"] = "indexação de arquivos do Windows",
        ["SearchProtocolHost"] = "indexação de arquivos do Windows",
        ["OneDrive"] = "OneDrive sincronizando",
        ["steamwebhelper"] = "janela/navegador interno da Steam",
        ["msedgewebview2"] = "páginas web embutidas (Xbox, widgets, Steam, etc.)",
        ["WmiPrvSE"] = "consultas de sensores do Windows (WMI)",
        ["TiWorker"] = "Windows Update instalando",
        ["MoUsoCoreWorker"] = "Windows Update",
        ["RTSS"] = "RivaTuner (HUD/limite de FPS)",
        ["XboxPcApp"] = "app Xbox em segundo plano",
        ["GameBar"] = "Xbox Game Bar",
    };

    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System Idle Process", "_Total",
    };

    public static PowerReportResult Build(IReadOnlyList<PowerSample> samples)
    {
        var battery = samples.Where(s => !s.OnAc && s.Seconds > 0).ToList();
        var minutes = battery.Sum(s => s.Seconds) / 60;
        if (battery.Count == 0) return new PowerReportResult();

        var avgW = WeightedAvg(battery, s => s.Watts is > 0 ? s.Watts : null);

        var byState = battery.GroupBy(s => s.State)
            .Select(g => new StateUsage(g.Key, g.Sum(s => s.Seconds) / 60,
                WeightedAvg(g, s => s.Watts is > 0 ? s.Watts : null),
                WeightedAvg(g, s => s.LauncherCpuPercent)))
            .OrderByDescending(x => x.Minutes)
            .ToList();

        var noGame = battery.Where(s => s.State != PowerState.Game).ToList();
        var launcherCpu = WeightedAvg(battery, s => s.LauncherCpuPercent);
        var launcherCpuNoGame = WeightedAvg(noGame, s => s.LauncherCpuPercent);

        // funções do launcher: totais divididos pelo tempo coberto
        var activities = new Dictionary<string, (double Ms, long Count)>(StringComparer.Ordinal);
        var activitySeconds = 0.0;
        foreach (var s in battery.Where(s => s.ActivityMs is not null))
        {
            activitySeconds += s.Seconds;
            foreach (var (name, ms) in s.ActivityMs!)
            {
                activities.TryGetValue(name, out var cur);
                long n = 0;
                s.ActivityCount?.TryGetValue(name, out n);
                activities[name] = (cur.Ms + ms, cur.Count + n);
            }
        }
        var activityList = activitySeconds <= 0 ? new List<ActivityCost>() : activities
            .Select(kv => new ActivityCost(kv.Key, kv.Value.Ms / (activitySeconds / 60), kv.Value.Count / (activitySeconds / 60)))
            .OrderByDescending(a => a.MsPerMinute)
            .ToList();

        var top = TopProcesses(battery, noGame);
        var accuracy = Accuracy(samples);
        var drop = PercentDrop(battery);

        var result = new PowerReportResult
        {
            BatteryMinutes = minutes,
            PercentDrop = drop,
            AvgWatts = avgW,
            ByState = byState,
            LauncherCpuPercent = launcherCpu,
            LauncherCpuPercentWithoutGame = launcherCpuNoGame,
            Activities = activityList,
            TopProcesses = top,
            Accuracy = accuracy,
        };
        result.Findings = Findings(result, battery, noGame);
        return result;
    }

    private static List<ProcessUsage> TopProcesses(List<PowerSample> battery, List<PowerSample> noGame)
    {
        var withProcs = battery.Where(s => s.TopProcesses is not null).ToList();
        var totalSeconds = withProcs.Sum(s => s.Seconds);
        if (totalSeconds <= 0) return new List<ProcessUsage>();
        var noGameSet = new HashSet<PowerSample>(noGame.Where(s => s.TopProcesses is not null));
        var noGameSeconds = noGameSet.Sum(s => s.Seconds);

        var all = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var idle = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in withProcs)
            foreach (var p in s.TopProcesses!)
            {
                if (IgnoredProcesses.Contains(p.Name)) continue;
                all[p.Name] = all.GetValueOrDefault(p.Name) + p.CpuPercent * s.Seconds;
                if (noGameSet.Contains(s)) idle[p.Name] = idle.GetValueOrDefault(p.Name) + p.CpuPercent * s.Seconds;
            }
        return all
            .Select(kv => new ProcessUsage(kv.Key, kv.Value / totalSeconds,
                noGameSeconds > 0 ? idle.GetValueOrDefault(kv.Key) / noGameSeconds : 0,
                kv.Key.Equals(LauncherProcessName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => p.CpuPercent)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Compara cada previsão com o ritmo que veio depois: pega uma amostra e outra 20–40 min
    /// adiante, no mesmo trecho na bateria (sem tomada nem suspensão no meio), e calcula quanto
    /// tempo a carga duraria naquele ritmo real. Erro = distância da previsão a esse valor.
    /// </summary>
    public static EstimateAccuracy Accuracy(IReadOnlyList<PowerSample> samples)
    {
        var winErr = new List<double>();
        var ourErr = new List<double>();
        var pairs = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            var a = samples[i];
            if (a.OnAc || a.RemainingMwh is not > 0) continue;
            for (var j = i + 1; j < samples.Count; j++)
            {
                var b = samples[j];
                if (b.OnAc || b.RemainingMwh is not > 0 || b.Seconds > BatteryLifeEstimator.MaxGap.TotalSeconds) break;
                var span = b.Utc - a.Utc;
                if (span < AccuracyMin) continue;
                if (span > AccuracyMax) break;
                var dropMwh = a.RemainingMwh.Value - b.RemainingMwh.Value;
                if (dropMwh < AccuracyMinDropMwh) break;
                var realW = dropMwh / 1000.0 / span.TotalHours;
                var realMin = a.RemainingMwh.Value / 1000.0 / realW * 60;
                pairs++;
                if (a.WindowsRemainingMinutes is > 0) winErr.Add(Math.Abs(a.WindowsRemainingMinutes.Value - realMin) / realMin * 100);
                if (a.OurRemainingMinutes is > 0) ourErr.Add(Math.Abs(a.OurRemainingMinutes.Value - realMin) / realMin * 100);
                break;
            }
        }
        return new EstimateAccuracy(pairs,
            winErr.Count > 0 ? winErr.Average() : null,
            ourErr.Count > 0 ? ourErr.Average() : null);
    }

    private static int? PercentDrop(List<PowerSample> battery)
    {
        // soma das quedas de cada trecho contínuo na bateria (a tomada no meio não desconta)
        var total = 0;
        int? prev = null;
        var any = false;
        foreach (var s in battery)
        {
            if (s.Percent is not { } p) continue;
            if (prev is { } q && p < q) { total += q - p; any = true; }
            prev = p;
        }
        return any ? total : null;
    }

    private static List<PowerFinding> Findings(PowerReportResult r, List<PowerSample> battery, List<PowerSample> noGame)
    {
        var f = new List<PowerFinding>();

        // 1) o próprio launcher
        if (r.LauncherCpuPercentWithoutGame is { } lc)
        {
            var who = string.Join(", ", r.Activities.Take(3).Select(a => a.Name));
            if (lc >= LauncherAttentionPercent)
                f.Add(new(FindingLevel.Attention, $"O launcher usou {lc:0.0}% do processador sem jogo aberto — mais do que deveria." +
                    (who.Length > 0 ? $" O que mais trabalhou: {who}." : "")));
            else if (lc >= LauncherInfoPercent)
                f.Add(new(FindingLevel.Info, $"O launcher usou {lc:0.0}% do processador sem jogo aberto (aceitável)." +
                    (who.Length > 0 ? $" O que mais trabalhou: {who}." : "")));
            else
                f.Add(new(FindingLevel.Info, $"O launcher quase não gastou: {lc:0.0}% do processador sem jogo aberto."));
        }

        // 2) outros programas sem jogo aberto
        foreach (var p in r.TopProcesses.Where(p => !p.IsLauncher && p.CpuPercentWithoutGame >= ProcessAttentionPercent).Take(4))
        {
            var why = KnownBackground.TryGetValue(p.Name, out var w) ? $" ({w})" : "";
            f.Add(new(FindingLevel.Attention, $"{p.Name} usou {p.CpuPercentWithoutGame:0.0}% do processador sem jogo aberto{why}."));
        }
        foreach (var p in r.TopProcesses.Where(p => !p.IsLauncher && p.CpuPercentWithoutGame is > 0.5 and < ProcessAttentionPercent
                                                    && KnownBackground.ContainsKey(p.Name)).Take(3))
            f.Add(new(FindingLevel.Info, $"{p.Name}: {KnownBackground[p.Name]} ({p.CpuPercentWithoutGame:0.0}% do processador sem jogo)."));

        // 3) consumo sem jogo
        var idleW = WeightedAvg(noGame, s => s.Watts is > 0 ? s.Watts : null);
        if (idleW is { } iw && iw >= IdleWattsAttention)
        {
            var causes = new List<string>();
            var bright = WeightedAvg(noGame, s => s.BrightnessPercent);
            var hz = WeightedAvg(noGame, s => s.RefreshHz);
            var tdp = WeightedAvg(noGame, s => s.TdpWatts);
            if (bright is >= BrightnessInfo) causes.Add($"brilho em {bright:0}%");
            if (hz is >= RefreshInfoHz) causes.Add($"tela a {hz:0} Hz");
            if (tdp is >= IdleTdpInfo) causes.Add($"TDP em {tdp:0} W");
            f.Add(new(FindingLevel.Attention, $"Sem jogo aberto o aparelho gastou {iw:0.0} W em média — alto para quem só está no launcher ou na área de trabalho." +
                (causes.Count > 0 ? $" Pesam: {string.Join(", ", causes)}." : "")));
        }
        else
        {
            var hz = WeightedAvg(noGame, s => s.RefreshHz);
            if (hz is >= RefreshInfoHz)
                f.Add(new(FindingLevel.Info, $"A tela ficou a {hz:0} Hz fora dos jogos; 60 Hz no launcher economiza um pouco."));
        }

        // 4) previsão de tempo
        if (r.Accuracy.WindowsErrorPercent is { } we && we >= 25)
            f.Add(new(FindingLevel.Info, $"O tempo restante do Windows errou {we:0}% em média em relação ao que aconteceu" +
                (r.Accuracy.OurErrorPercent is { } oe ? $"; a conta do launcher errou {oe:0}%." : ".")));

        return f;
    }

    private static double? WeightedAvg(IEnumerable<PowerSample> samples, Func<PowerSample, double?> value)
    {
        double sum = 0, weight = 0;
        foreach (var s in samples)
        {
            if (value(s) is not { } v) continue;
            sum += v * s.Seconds;
            weight += s.Seconds;
        }
        return weight > 0 ? sum / weight : null;
    }

    private static double? WeightedAvg(IEnumerable<PowerSample> samples, Func<PowerSample, int?> value) =>
        WeightedAvg(samples, s => value(s) is { } v ? (double?)v : null);
}
