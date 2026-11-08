# Arquitetura

```
src/
  PcMax.Core/      Lógica pura, testável em qualquer SO (sem chamadas ao Windows)
    Tweaks/        ITweak + TweakEngine: aplica e reverte com snapshots persistidos e validados
    Doctor/        HardwareProfile -> DoctorEngine -> Findings (gargalos reais)
    Alerts/        Limites de temperatura + AlertEngine (sustentação, histerese, cooldown)
    Benchmark/     FrameStats (FPS, 1% lows) e comparação antes/depois com margem de ruído
    Network/       NetworkStats (ping, jitter, perda) e nota de bufferbloat
    Games/         Perfis por jogo, jogos conhecidos, GameSessionManager (modo jogo)
    Settings/      AppSettings (validado), AppPaths, escrita atómica, processos protegidos
    Updates/       ReleaseVersion, ReleaseAssets (nomes e .sha256), ReleaseFeed (resposta da API do GitHub)
    Platform/      Interfaces que a camada Windows implementa
  PcMax.Windows/   Implementações Windows: sensores (LibreHardwareMonitor), WMI, energia (powrprof),
                   registo, ecrã, processos, arranque, limpeza, restauro e
    System/        SecureDataFolder (ACL de %ProgramData%\PcMax + migração), TaskSchedulerAutostart (COM),
                   InstallLocation (Program Files), Authenticode (WinVerifyTrust)
  PcMax.App/       Interface Avalonia (MVVM), bandeja, pop-ups, UpdateService (GitHub Releases),
                   ShellDesktopLauncher (abre links sem privilégios)
installer/
  pcmax.iss        Instalador Inno Setup por máquina (Program Files, só x64)
tests/
  PcMax.Core.Tests/
tools/
  Screenshots/     Desenha cada página sem ecrã (plataforma simulada)
```

`PlatformFactory` (PcMax.App) é o único sítio que chama o PcMax.Windows; fora do Windows (ou com `PCMAX_FAKE=1` numa build Debug) a app usa a plataforma simulada.

## Arranque

1. `SetDefaultDllDirectories` (só System32 e a pasta da app).
2. `--uninstall-cleanup`: sem interface nem instância única; retoma apps pausadas, reverte todos os ajustes (máx. 20 s), remove a tarefa de arranque e sai com 0.
3. Instância única por utilizador (`Local\PcMax-<SID>`); um segundo arranque só pede à janela aberta para aparecer.
4. `SecureDataFolder.Ensure`: verifica/recria `%ProgramData%\PcMax` (e `logs`, `updates`) com permissões só de administrador e migra uma vez os dados de `%LOCALAPPDATA%\PcMax`. Só depois se lê estado ou se abre o registo.
5. Interface; em segundo plano: se houver uma atualização já transferida e verificada, instala-a (antes do detetor de jogos arrancar); senão recupera uma sessão de jogo interrompida, sincroniza o arranque com o Windows (só a cópia em Program Files) e arranca o detetor de jogos, os sensores e o verificador de atualizações.

## Modo jogo

`IGameWatcher` deteta o jogo, e o `GameSessionManager` aplica o perfil (ajustes `GameSessionOnly`, prioridade alta, pausa das apps em segundo plano). Quando o jogo fecha, reverte tudo. Os snapshots ficam em disco, por isso um crash é recuperado no arranque seguinte; ao carregar, cada snapshot é validado pelo seu ajuste (`ITweak.AcceptsSnapshot`).

## Atualizações

`UpdateService`: API `releases/latest` → `ReleaseFeed` escolhe `PcMax-Setup-<ver>.exe` e `.sha256` → transferência para `updates\` com SHA-256 (e assinatura Authenticode do mesmo certificado, se a app em execução for assinada) → aviso "Instalar agora" ou instalação no próximo arranque → instalador com `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` e a app fecha. O instalador volta a abrir a app com `--minimized`. Nunca durante um jogo.

## Release

`.github/workflows/release.yml` (tag `vX.Y.Z`): testes → `dotnet publish` win-x64 self-contained → PresentMon fixo e com assinatura da Intel verificada → falha se houver `*.sys` ou WinRing0 → assinatura opcional → `iscc installer/pcmax.iss` → `.sha256` → `gh release create`.
