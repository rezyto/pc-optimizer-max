using System.Management;
using System.Runtime.Versioning;
using PcMax.Core.Platform;

namespace PcMax.Windows.SystemServices;

/// <summary>
/// System Restore through WMI (root\default:SystemRestore). Windows creates at most one point per
/// 24 h by default (SystemRestorePointCreationFrequency, left untouched): in that case the call
/// "succeeds" without creating anything, so we compare the newest sequence number before and after.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SystemRestorePoints : IRestorePoints
{
    private const uint ModifySettings = 12;      // MODIFY_SETTINGS
    private const uint BeginSystemChange = 100;  // BEGIN_SYSTEM_CHANGE

    public Task<bool> CreateAsync(string description, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var desc = string.IsNullOrWhiteSpace(description) ? "PC Max" : description.Trim();
            if (desc.Length > 64) desc = desc[..64];

            var before = LatestSequence();
            var scope = new ManagementScope(@"\\.\root\default");
            using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = desc;
            inParams["RestorePointType"] = ModifySettings;
            inParams["EventType"] = BeginSystemChange;
            using var result = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            var rc = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
            if (rc != 0)
            {
                Log.Warn($"Ponto de restauro não criado (código {rc}; o Restauro do Sistema pode estar desligado).");
                return false;
            }
            var after = LatestSequence();
            if (after is null || after == before)
            {
                Log.Info("Ponto de restauro não criado: o Windows só permite um a cada 24 horas.");
                return false;
            }
            Log.Info($"Ponto de restauro criado (#{after}).");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Criação do ponto de restauro falhou", ex);
            return false;
        }
    }, ct);

    private static uint? LatestSequence()
    {
        try
        {
            uint? max = null;
            using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\default"), new ObjectQuery("SELECT SequenceNumber FROM SystemRestore"));
            using var results = s.Get();
            foreach (ManagementObject o in results)
            {
                using (o)
                {
                    if (o["SequenceNumber"] is uint n && (max is null || n > max)) max = n;
                }
            }
            return max;
        }
        catch { return null; }
    }
}
