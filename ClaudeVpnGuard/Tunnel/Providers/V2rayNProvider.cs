namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class V2rayNProvider : ITunnelProvider
{
    public string Id => "v2rayn";
    public string DisplayName => "v2rayN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("v2rayN", "v2rayn");
        var hasCore = snapshot.HasProcess("xray", "sing-box", "singbox", "wv2ray");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "v2ray", "sing-box", "wintun", "tun");

        // Avoid claiming Happ's xray as v2rayN
        var hasHapp = snapshot.HasProcess("Happ", "happd") ||
                      ProviderHelpers.HasUpAdapter(snapshot, "happ");

        if (hasApp && hasCore)
            return new TunnelProbeResult(true, "v2rayN + core", adapter?.Name);

        if (hasApp && adapter is not null)
            return new TunnelProbeResult(true, $"v2rayN + {adapter.Name}", adapter.Name);

        if (!hasHapp && hasApp)
            return new TunnelProbeResult(true, "v2rayN process (proxy mode possible)");

        return new TunnelProbeResult(false, "v2rayN tunnel not detected");
    }
}
