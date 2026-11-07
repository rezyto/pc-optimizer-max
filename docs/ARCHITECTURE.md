# Arquitetura

```
src/
  PcMax.Core/      Lógica pura, testável em qualquer SO (sem chamadas ao Windows)
    Tweaks/        ITweak + TweakEngine: aplica e reverte com snapshots persistidos
    Doctor/        HardwareProfile -> DoctorEngine -> Findings (gargalos reais)
    Alerts/        Limites de temperatura + AlertEngine (sustentação, histerese, cooldown)
    Benchmark/     FrameStats (FPS, 1% lows) e comparação antes/depois com margem de ruído
    Network/       NetworkStats (ping, jitter, perda) e nota de bufferbloat
    Games/         Perfis por jogo, jogos conhecidos, GameSessionManager (modo jogo)
    Settings/      AppSettings (validado), escrita atómica, processos protegidos
    Platform/      Interfaces que a camada Windows implementa
  PcMax.Windows/   Implementações Windows: sensores (LibreHardwareMonitor), WMI, energia,
                   registo, ecrã, processos, arranque, limpeza, Task Scheduler, restauro
  PcMax.App/       Interface Avalonia (MVVM), bandeja, pop-ups, atualizações (Velopack)
tests/
  PcMax.Core.Tests/
```

Fluxo do modo jogo: `IGameWatcher` deteta o jogo, e o `GameSessionManager` aplica o perfil (ajustes `GameSessionOnly`, prioridade alta, pausa das apps em segundo plano). Quando o jogo fecha, reverte tudo. Os snapshots ficam em disco, por isso um crash é recuperado no arranque seguinte.
