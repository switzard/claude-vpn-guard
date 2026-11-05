namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class ShadowsocksProvider : ITunnelProvider
{
    public string Id => "shadowsocks";
    public string DisplayName => "Shadowsocks";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("Shadowsocks", "shadowsocks", "ss-local");
        var hasCore = snapshot.HasProcessContains("ss-local", "shadowsocks");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "shadowsocks", "ss-tun", "sstap");

        if ((hasApp || hasCore) && adapter is not null)
            return new TunnelProbeResult(true, $"Shadowsocks + {adapter.Name}", adapter.Name);

        if (hasApp || hasCore)
            return new TunnelProbeResult(false, "Shadowsocks running without TUN", Degraded: true);

        return new TunnelProbeResult(false, "Shadowsocks tunnel not detected");
    }
}
