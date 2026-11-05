namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class ClashVergeProvider : ITunnelProvider
{
    public string Id => "clash-verge";
    public string DisplayName => "Clash Verge Rev";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("clash-verge", "Clash Verge", "ClashVerge");
        var hasCore = snapshot.HasProcessContains("mihomo", "clash-meta", "clash");
        // Drop short/shared "meta" / generic "wintun" — require Clash-ish NIC names.
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "mihomo", "clash", "Clash");

        if (hasApp && hasCore && adapter is not null)
            return new TunnelProbeResult(true, $"Clash + {adapter.Name}", adapter.Name);

        // App or core alone with named adapter (no shared wintun claim)
        if ((hasApp || hasCore) && adapter is not null &&
            (adapter.Name.Contains("clash", StringComparison.OrdinalIgnoreCase) ||
             adapter.Name.Contains("mihomo", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("clash", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("mihomo", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(true, $"Clash + {adapter.Name}", adapter.Name);

        if (hasApp || hasCore)
            return new TunnelProbeResult(false, "Clash running but no TUN — not connected", Degraded: true);

        return new TunnelProbeResult(false, "Clash Verge tunnel not detected");
    }
}
