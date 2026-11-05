namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class HappProvider : ITunnelProvider
{
    public string Id => "happ";
    public string DisplayName => "Happ";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "happ-xray", "happ");
        var hasClient = snapshot.HasProcess("Happ", "happd");
        var hasCore = snapshot.HasProcess("xray", "happd");

        // Connected only when TUN adapter is Up AND client/core is alive.
        if (adapter is not null && (hasClient || hasCore))
            return new TunnelProbeResult(true, $"adapter {adapter.Name} + core", adapter.Name);

        if (adapter is not null)
            return new TunnelProbeResult(false, $"adapter {adapter.Name} without Happ/xray — not connected", adapter.Name, Degraded: true);

        if (hasClient || hasCore)
            return new TunnelProbeResult(false, "Happ installed/running but tunnel adapter down", Degraded: true);

        return new TunnelProbeResult(false, "Happ tunnel not detected");
    }
}
