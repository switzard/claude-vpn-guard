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

        if (adapter is not null && (hasClient || hasCore))
            return new TunnelProbeResult(true, $"adapter {adapter.Name} + core", adapter.Name);

        if (adapter is not null)
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasClient && hasCore)
            return new TunnelProbeResult(true, "Happ process + xray/happd");

        return new TunnelProbeResult(false, "Happ tunnel not detected");
    }
}
