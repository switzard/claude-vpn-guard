namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class NekorayProvider : ITunnelProvider
{
    public string Id => "nekoray";
    public string DisplayName => "Nekoray / NekoBox";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("nekoray", "nekobox", "NekoRay", "NekoBox");
        var hasCore = snapshot.HasProcess("sing-box", "singbox", "nekobox_core");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "nekoray", "nekobox", "sing-box", "wintun");

        if (hasApp && (hasCore || adapter is not null))
            return new TunnelProbeResult(true, adapter is null ? "Nekoray + core" : $"Nekoray + {adapter.Name}", adapter?.Name);

        return new TunnelProbeResult(false, "Nekoray tunnel not detected");
    }
}
