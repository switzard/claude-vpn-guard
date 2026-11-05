namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class V2rayNProvider : ITunnelProvider
{
    public string Id => "v2rayn";
    public string DisplayName => "v2rayN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("v2rayN", "v2rayn");
        var hasCore = snapshot.HasProcess("xray", "sing-box", "singbox", "wv2ray");
        // No bare "tun" — matches foreign TUNs. Prefer named / v2ray / sing-box NICs.
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "v2ray", "sing-box", "singbox");

        var hasHapp = snapshot.HasProcess("Happ", "happd") ||
                      ProviderHelpers.HasUpAdapter(snapshot, "happ");

        if (hasHapp)
            return new TunnelProbeResult(false, "Happ owns xray/adapter — skip v2rayN");

        if (hasApp && hasCore && adapter is not null)
            return new TunnelProbeResult(true, $"v2rayN + {adapter.Name}", adapter.Name);

        if (hasApp || hasCore)
            return new TunnelProbeResult(false, "v2rayN open but no TUN — not connected", Degraded: true);

        return new TunnelProbeResult(false, "v2rayN tunnel not detected");
    }
}
