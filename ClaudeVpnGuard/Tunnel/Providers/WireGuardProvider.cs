namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class WireGuardProvider : ITunnelProvider
{
    public string Id => "wireguard";
    public string DisplayName => "WireGuard";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("wireguard");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "WireGuard", "wg-");

        // Don't steal AmneziaWG adapters if Amnezia is clearly active
        if (adapter is not null &&
            adapter.Name.Contains("Amnezia", StringComparison.OrdinalIgnoreCase))
        {
            return new TunnelProbeResult(false, "AmneziaWG adapter owned by Amnezia provider");
        }

        if (adapter is not null)
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasApp)
            return new TunnelProbeResult(false, "WireGuard app open but no Up tunnel adapter");

        return new TunnelProbeResult(false, "WireGuard tunnel not detected");
    }
}
