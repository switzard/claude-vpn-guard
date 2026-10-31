namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class ClashVergeProvider : ITunnelProvider
{
    public string Id => "clash-verge";
    public string DisplayName => "Clash Verge Rev";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("clash-verge", "Clash Verge", "ClashVerge");
        var hasCore = snapshot.HasProcessContains("mihomo", "clash-meta", "clash");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "mihomo", "clash", "meta", "wintun");

        if ((hasApp || hasCore) && adapter is not null)
            return new TunnelProbeResult(true, $"Clash + {adapter.Name}", adapter.Name);

        if (hasApp && hasCore)
            return new TunnelProbeResult(true, "Clash Verge + mihomo/clash core");

        return new TunnelProbeResult(false, "Clash Verge tunnel not detected");
    }
}
