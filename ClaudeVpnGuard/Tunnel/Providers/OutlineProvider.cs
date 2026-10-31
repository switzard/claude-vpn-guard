namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class OutlineProvider : ITunnelProvider
{
    public string Id => "outline";
    public string DisplayName => "Outline";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("Outline");
        var hasCore = snapshot.HasProcessContains("ss-local", "tun2socks", "outline-client");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "outline", "tun2socks", "ss-local");

        if (hasApp && (hasCore || adapter is not null))
            return new TunnelProbeResult(true, adapter is null ? "Outline connected" : $"Outline + {adapter.Name}", adapter?.Name);

        return new TunnelProbeResult(false, "Outline tunnel not detected");
    }
}
