using PcMax.Core.Platform;
using PcMax.Core.Tweaks;

namespace PcMax.App.Services.Fakes;

/// <summary>A believable fake PC for running the UI on Linux/macOS and for screenshots.</summary>
public static class FakePlatform
{
    public static PlatformServices Create()
    {
        var state = new FakeState();
        var tweaks = new ITweak[]
        {
            new FakeTweak(state, "display.max-refresh", "Frequência máxima do monitor",
                "Põe o monitor na frequência mais alta que suporta (ex.: 144 Hz).", RiskLevel.Safe, TweakCategory.Display, false),
            new FakeTweak(state, "power.high-performance", "Plano de energia de alto desempenho",
                "Durante o jogo o CPU responde mais depressa. O teu plano volta quando sais.", RiskLevel.Safe, TweakCategory.Power, true),
            new FakeTweak(state, "latency.timer-resolution", "Temporizador do Windows de alta precisão",
                "Reduz pequenos atrasos entre imagens enquanto jogas.", RiskLevel.Safe, TweakCategory.Latency, true),
            new FakeTweak(state, "windows.game-mode", "Modo de Jogo do Windows",
                "Dá prioridade ao jogo e adia o Windows Update durante a partida.", RiskLevel.Safe, TweakCategory.Background, false),
            new FakeTweak(state, "windows.disable-background-recording", "Desligar gravação em segundo plano",
                "A Xbox Game Bar deixa de gravar os últimos minutos sem pedires.", RiskLevel.Safe, TweakCategory.Gpu, false),
            new FakeTweak(state, "network.disable-nagle", "Envio imediato de pacotes",
                "Desliga o agrupamento de pacotes TCP em alguns jogos.", RiskLevel.Safe, TweakCategory.Network, true),
            new FakeTweak(state, "gpu.undervolt", "Undervolt da placa gráfica",
                "Menos calor e ruído com o mesmo desempenho.", RiskLevel.Advanced, TweakCategory.Gpu, false),
        };
        return new PlatformServices(
            new FakeSensorProvider(state),
            new FakeHardwareInspector(state),
            tweaks,
            new FakeGameWatcher(),
            new FakeProcessBooster(),
            new FakeStartupManager(),
            new FakeCleaner(),
            new FakeAutostart(),
            new FakeRestorePoints(),
            new FakeNetworkProbe(),
            new FakeFrameCapture(state));
    }
}

/// <summary>Shared state between fakes, so applying a tweak changes what the inspector reports.</summary>
public sealed class FakeState
{
    private readonly HashSet<string> _applied = new();
    private readonly object _gate = new();
    public bool IsApplied(string id) { lock (_gate) return _applied.Contains(id); }
    public void Set(string id, bool on) { lock (_gate) { if (on) _applied.Add(id); else _applied.Remove(id); } }
}
