using PcMax.Core.Benchmark;
using PcMax.Core.Games;
using PcMax.Core.Network;
using PcMax.Core.Sensors;
using PcMax.Core.Tweaks;

namespace PcMax.Core.Platform;

/// <summary>
/// Everything the UI needs from the operating system. On Windows it is built by
/// <c>PcMax.Windows.WindowsPlatform.Create(paths)</c>; elsewhere the app uses fakes (for design and screenshots).
/// </summary>
public sealed record PlatformServices(
    ISensorProvider Sensors,
    IHardwareInspector Hardware,
    IReadOnlyList<ITweak> Tweaks,
    IGameWatcher GameWatcher,
    IProcessBooster Booster,
    IStartupManager Startup,
    ICleaner Cleaner,
    IAutostart Autostart,
    IRestorePoints RestorePoints,
    INetworkProbe Network,
    IFrameCapture FrameCapture) : IDisposable
{
    public void Dispose()
    {
        Sensors.Dispose();
        GameWatcher.Dispose();
    }
}
