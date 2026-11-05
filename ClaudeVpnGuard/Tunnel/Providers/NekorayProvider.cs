namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class NekorayProvider : ITunnelProvider
{
    public string Id => "nekoray";
    public string DisplayName => "Nekoray / NekoBox";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("nekoray", "nekobox", "NekoRay", "NekoBox");
        var hasCore = snapshot.HasProcess("sing-box", "singbox", "nekobox_core");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "nekoray", "nekobox", "sing-box");

        if (hasApp && adapter is not null)
            return new TunnelProbeResult(true, $"Nekoray + {adapter.Name}", adapter.Name);

        if (hasApp || hasCore)
            return new TunnelProbeResult(false, "Nekoray open but no TUN — not connected", Degraded: true);

        return new TunnelProbeResult(false, "Nekoray tunnel not detected");
    }
}
