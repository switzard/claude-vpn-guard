namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class AmneziaProvider : ITunnelProvider
{
    public string Id => "amnezia";
    public string DisplayName => "AmneziaVPN / AmneziaWG";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("AmneziaVPN", "AmneziaWG", "Amnezia");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "Amnezia", "amn0");

        if (adapter is not null)
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasApp && snapshot.HasProcessContains("AmneziaWG", "wireguard-service", "AmneziaVPN-service"))
            return new TunnelProbeResult(true, "Amnezia processes active");

        return new TunnelProbeResult(false, "Amnezia tunnel not detected");
    }
}
