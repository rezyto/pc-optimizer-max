namespace PcMax.Core.Doctor;

/// <summary>
/// Finds the real bottlenecks of a PC. Configuration problems (RAM speed, refresh rate, wrong GPU,
/// heat) usually cost far more performance than any software tweak can win back, so they are
/// checked first. Pure logic: input is a <see cref="HardwareProfile"/>, output is a ranked list.
/// </summary>
public static class DoctorEngine
{
    public static IReadOnlyList<Finding> Analyze(HardwareProfile p)
    {
        var f = new List<Finding>();
        CheckMonitors(p, f);
        CheckRamSpeed(p, f);
        CheckRamChannels(p, f);
        CheckGpu(p, f);
        CheckPower(p, f);
        CheckHeat(p, f);
        CheckWindows(p, f);
        CheckStorage(p, f);
        CheckNetwork(p, f);
        return f.OrderByDescending(x => x.Severity).ThenByDescending(x => x.ImpactScore).ToList();
    }

    /// <summary>0–100 health score: 100 minus weighted penalties.</summary>
    public static int HealthScore(IReadOnlyList<Finding> findings)
    {
        var penalty = findings.Sum(x => x.Severity switch
        {
            FindingSeverity.Critical => 25,
            FindingSeverity.Problem => 12,
            FindingSeverity.Tip => 3,
            _ => 0,
        });
        return Math.Clamp(100 - penalty, 0, 100);
    }

    private static void CheckMonitors(HardwareProfile p, List<Finding> f)
    {
        foreach (var m in p.Monitors)
        {
            if (m.MaxRefreshHz > m.CurrentRefreshHz && m.MaxRefreshHz - m.CurrentRefreshHz >= 10)
            {
                f.Add(new Finding(
                    $"monitor-hz-{m.Name}",
                    FindingSeverity.Critical,
                    $"Monitor a {m.CurrentRefreshHz} Hz em vez de {m.MaxRefreshHz} Hz",
                    $"O ecrã \"{m.Name}\" suporta {m.MaxRefreshHz} Hz mas o Windows está a usar {m.CurrentRefreshHz} Hz. Estás a ver menos imagens por segundo do que o teu monitor consegue mostrar, e o jogo parece menos fluido mesmo com FPS altos.",
                    $"Até {m.MaxRefreshHz} imagens por segundo em vez de {m.CurrentRefreshHz}",
                    FixAction.Auto("display.max-refresh"),
                    95));
            }
        }
    }

    private static void CheckRamSpeed(HardwareProfile p, List<Finding> f)
    {
        var mods = p.MemoryModules.Where(m => m.ConfiguredSpeedMhz > 0 && m.RatedSpeedMhz > 0).ToList();
        if (mods.Count == 0) return;
        var configured = mods.Min(m => m.ConfiguredSpeedMhz!.Value);
        var rated = mods.Min(m => m.RatedSpeedMhz!.Value);
        // JEDEC base speeds: DDR4 2133/2400, DDR5 4800. If running well below the rated (XMP/EXPO) speed, flag it.
        if (rated - configured >= 400)
        {
            f.Add(new Finding(
                "ram-xmp",
                FindingSeverity.Critical,
                $"RAM a {configured} MHz em vez de {rated} MHz (XMP/EXPO desligado)",
                "A tua memória foi feita para trabalhar mais rápido, mas o perfil XMP (Intel) ou EXPO (AMD) está desligado no BIOS. Isto é dos maiores ganhos possíveis em jogos que dependem do CPU.",
                "+10–25% nos 1% lows (menos travões) em muitos jogos",
                FixAction.Guide(
                    "Reinicia o PC e entra no BIOS (normalmente a tecla DEL ou F2 durante o arranque).",
                    "Procura a opção XMP, EXPO, DOCP ou A-XMP (muitas vezes em 'AI Tweaker', 'Extreme Tweaker' ou 'OC').",
                    "Ativa o Perfil 1.",
                    "Guarda e sai (normalmente F10).",
                    "Se o PC não arrancar, ele volta sozinho às definições anteriores após 3 tentativas. Nesse caso, tenta o Perfil 2 ou mantém desligado."),
                90));
        }
    }

    private static void CheckRamChannels(HardwareProfile p, List<Finding> f)
    {
        if (p.MemoryModules.Count == 1 && !p.IsLaptop)
        {
            f.Add(new Finding(
                "ram-single-channel",
                FindingSeverity.Problem,
                "Memória em canal único (só 1 módulo)",
                "Com um só módulo de RAM, o CPU tem metade da largura de banda de memória. Dois módulos iguais (dual channel) dão um ganho grande em jogos.",
                "+10–30% de FPS em jogos que dependem do CPU",
                FixAction.Guide(
                    "Compra um segundo módulo igual ao atual (mesma capacidade e velocidade), ou um kit de 2.",
                    "Instala-os nas ranhuras indicadas no manual da motherboard (normalmente A2 e B2, a 2.ª e a 4.ª a contar do CPU)."),
                80));
        }
        else if (p.MemoryModules.Count == 2 && !p.IsLaptop)
        {
            // Two sticks in slots of the same channel (e.g. A1+A2) run in single channel.
            var channels = p.MemoryModules.Select(m => ChannelOf(m.Slot)).ToList();
            if (channels.All(c => c is not null) && channels.Distinct().Count() == 1)
            {
                f.Add(new Finding(
                    "ram-wrong-slots",
                    FindingSeverity.Problem,
                    "Os 2 módulos de RAM parecem estar no mesmo canal",
                    "Os módulos parecem estar em ranhuras do mesmo canal, o que os faz funcionar em canal único. Mudá-los de ranhura é grátis e pode dar um ganho grande.",
                    "+10–30% de FPS em jogos que dependem do CPU",
                    FixAction.Guide(
                        "Desliga o PC da corrente.",
                        "Move os módulos para as ranhuras recomendadas no manual (normalmente A2 e B2).",
                        "Volta a ligar e confirma aqui no Médico."),
                    70));
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex[] ChannelPatterns =
    {
        new(@"CHANNEL\s*([A-D])", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"DIMM_?([A-D])\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"^([A-D])\d$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
    };

    /// <summary>Channel letter from a slot label like "DIMM_A2", "ChannelB-DIMM0" or "A1". Null if unknown.</summary>
    internal static char? ChannelOf(string slot)
    {
        foreach (var re in ChannelPatterns)
        {
            var m = re.Match(slot.Trim());
            if (m.Success) return char.ToUpperInvariant(m.Groups[1].Value[0]);
        }
        return null;
    }

    private static void CheckGpu(HardwareProfile p, List<Finding> f)
    {
        if (p.Gpus.Count == 0) return;
        if (p.DaysSinceGpuDriver is > 180)
        {
            f.Add(new Finding(
                "gpu-driver-old",
                FindingSeverity.Tip,
                "Driver da placa gráfica com mais de 6 meses",
                "Os drivers novos trazem otimizações para jogos recentes e correções de estabilidade.",
                "Varia por jogo; em jogos novos pode ser significativo",
                FixAction.Guide("Abre a app NVIDIA (ou AMD Adrenalin / Intel Arc Control) e instala o driver mais recente."),
                40));
        }
    }

    private static void CheckPower(HardwareProfile p, List<Finding> f)
    {
        if (p.IsLaptop && p.OnBattery == true)
        {
            f.Add(new Finding(
                "laptop-battery",
                FindingSeverity.Problem,
                "Portátil a funcionar com bateria",
                "Com bateria, o CPU e a gráfica ficam limitados para poupar energia. Liga o carregador para jogar.",
                "Muitas vezes +30–50% de desempenho",
                FixAction.Guide("Liga o carregador antes de jogar."),
                85));
        }
        if (p.ActivePowerPlanIsHighPerformance == false)
        {
            f.Add(new Finding(
                "power-plan",
                FindingSeverity.Tip,
                $"Plano de energia: {p.ActivePowerPlanName ?? "poupança/equilibrado"}",
                "Durante os jogos, a app muda para o plano de alto desempenho para o CPU responder mais depressa, e volta ao teu plano quando sais.",
                "Menos micro-travões, sobretudo em portáteis",
                FixAction.Auto("power.high-performance"),
                45));
        }
    }

    private static void CheckHeat(HardwareProfile p, List<Finding> f)
    {
        if (p.MaxRecentCpuTempC is >= 90)
            f.Add(new Finding("cpu-hot", FindingSeverity.Critical, $"CPU chegou aos {p.MaxRecentCpuTempC:0} °C",
                "O processador está a aquecer tanto que baixa a velocidade para se proteger (throttling). Isto provoca quebras de FPS.",
                "Recuperar a velocidade máxima do CPU",
                FixAction.Guide("Limpa o pó das ventoinhas e do dissipador.", "Confirma que as ventoinhas da caixa rodam.",
                    "Num portátil, usa-o numa superfície dura ou com base de refrigeração.",
                    "Se o PC tiver mais de 3 anos, considera trocar a pasta térmica."), 85));
        if (p.MaxRecentGpuTempC is >= 87)
            f.Add(new Finding("gpu-hot", FindingSeverity.Problem, $"Placa gráfica chegou aos {p.MaxRecentGpuTempC:0} °C",
                "A gráfica está quente demais e baixa a velocidade.",
                "Recuperar a velocidade máxima da gráfica",
                FixAction.Guide("Limpa o pó da gráfica.", "Melhora a entrada de ar da caixa.", "Considera um undervolt (Modo Avançado)."), 70));
    }

    private static void CheckWindows(HardwareProfile p, List<Finding> f)
    {
        if (p.WindowsGameModeEnabled == false)
            f.Add(new Finding("win-game-mode", FindingSeverity.Tip, "Modo de Jogo do Windows desligado",
                "O Modo de Jogo dá prioridade ao jogo e impede o Windows Update de instalar durante a partida.",
                "Menos interrupções e travões", FixAction.Auto("windows.game-mode"), 35));
        if (p.GameDvrBackgroundRecording == true)
            f.Add(new Finding("game-dvr", FindingSeverity.Tip, "Gravação em segundo plano da Xbox Game Bar ligada",
                "O Windows está sempre a gravar os últimos minutos do jogo, o que gasta recursos da gráfica.",
                "+2–5% de FPS", FixAction.Auto("windows.disable-background-recording"), 30));
        if (p.StartupAppsEnabled is > 12)
            f.Add(new Finding("startup-many", FindingSeverity.Tip, $"{p.StartupAppsEnabled} programas abrem com o Windows",
                "Muitos programas no arranque ficam a gastar memória e CPU em segundo plano.",
                "Arranque mais rápido e mais RAM livre", FixAction.Guide("Vai a 'Limpeza e arranque' e desliga o que não usas."), 25));
    }

    private static void CheckStorage(HardwareProfile p, List<Finding> f)
    {
        if (p.SystemDriveFreeGb is < 15)
            f.Add(new Finding("disk-full", FindingSeverity.Problem, $"Só {p.SystemDriveFreeGb:0} GB livres no disco do sistema",
                "Com o disco quase cheio, o Windows fica lento e os jogos podem travar ao carregar.",
                "Menos travões e carregamentos mais rápidos", FixAction.Guide("Vai a 'Limpeza e arranque' e corre a limpeza."), 50));
        if (p.SystemDriveIsHdd == true)
            f.Add(new Finding("hdd", FindingSeverity.Problem, "O Windows está num disco rígido (HDD)",
                "Um SSD é 5 a 10 vezes mais rápido. É a melhoria que mais se nota no dia a dia e nos carregamentos.",
                "Carregamentos 3–5x mais rápidos", FixAction.Guide("Considera instalar o Windows e os jogos num SSD."), 60));
    }

    private static void CheckNetwork(HardwareProfile p, List<Finding> f)
    {
        if (p.NetworkIsWifi == true)
            f.Add(new Finding("wifi", FindingSeverity.Tip, "Estás ligado por Wi-Fi",
                "O Wi-Fi tem mais variação de ping (jitter) e perda de pacotes do que o cabo. Para jogos online, o cabo é o maior ganho de rede possível.",
                "Ping mais estável e menos picos de lag", FixAction.Guide("Usa um cabo de rede ligado ao router, ou um kit powerline se o router estiver longe."), 55));
    }
}
