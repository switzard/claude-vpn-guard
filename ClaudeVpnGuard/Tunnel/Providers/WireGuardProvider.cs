namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class WireGuardProvider : ITunnelProvider
{
    public string Id => "wireguard";
    public string DisplayName => "WireGuard";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("wireguard");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "WireGuard", "wg-");

        if (adapter is not null &&
            adapter.Name.Contains("Amnezia", StringComparison.OrdinalIgnoreCase))
        {
            return new TunnelProbeResult(false, "AmneziaWG adapter owned by Amnezia provider");
        }

        // Require WireGuard process/service host — adapter alone is not enough (stale NIC false Up).
        if (hasApp && adapter is not null &&
            (adapter.Name.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
             adapter.Name.Contains("wg-", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)))
        {
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);
        }

        if (adapter is not null &&
            (adapter.Name.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(false, "WireGuard adapter without wireguard process", Degraded: true);

        if (hasApp)
            return new TunnelProbeResult(false, "WireGuard app open but no Up tunnel adapter", Degraded: true);

        return new TunnelProbeResult(false, "WireGuard tunnel not detected");
    }
}
