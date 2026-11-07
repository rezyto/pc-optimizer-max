using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using PcMax.Core.Network;

namespace PcMax.Windows.Network;

/// <summary>ICMP ping (1000 ms timeout). The host must be a plain hostname or IP address.</summary>
[SupportedOSPlatform("windows")]
internal sealed class PingProbe : INetworkProbe
{
    private const int TimeoutMs = 1000;

    public async Task<IReadOnlyList<double?>> PingAsync(string host, int count, TimeSpan interval, CancellationToken ct = default)
    {
        var target = await ResolveAsync(host, ct).ConfigureAwait(false);
        count = Math.Clamp(count, 1, 1000);
        if (interval < TimeSpan.Zero) interval = TimeSpan.Zero;
        var results = new List<double?>(count);
        if (target is null)
        {
            for (var i = 0; i < count; i++) results.Add(null);
            return results;
        }

        using var ping = new Ping();
        var buffer = new byte[32];
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(TimeoutMs), buffer, null, ct).ConfigureAwait(false);
                results.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch (OperationCanceledException) { throw; }
            catch (PingException) { results.Add(null); }
            if (i < count - 1 && interval > TimeSpan.Zero)
                await Task.Delay(interval, ct).ConfigureAwait(false);
        }
        return results;
    }

    /// <summary>Validates the host and resolves it once (IPv4 preferred). Null if invalid/unresolvable.</summary>
    private static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct)
    {
        if (!IsValidHost(host)) throw new ArgumentException("Endereço inválido: usa um nome (ex.: 1.1.1.1 ou google.com).", nameof(host));
        host = host.Trim();
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            return addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.FirstOrDefault();
        }
        catch (SocketException) { return null; }
    }

    internal static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim();
        if (host.Length > 253) return false;
        if (IPAddress.TryParse(host, out _)) return Uri.CheckHostName(host) is UriHostNameType.IPv4 or UriHostNameType.IPv6;
        return Uri.CheckHostName(host) == UriHostNameType.Dns;
    }
}
