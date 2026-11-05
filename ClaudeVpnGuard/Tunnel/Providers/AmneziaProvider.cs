namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class AmneziaProvider : ITunnelProvider
{
    public string Id => "amnezia";
    public string DisplayName => "AmneziaVPN / AmneziaWG";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("AmneziaVPN", "AmneziaWG", "Amnezia");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "Amnezia", "amn0");

        // Never Healthy on adapter alone — stale NIC would false-Up Claude.
        if (adapter is not null && hasApp)
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (adapter is not null)
            return new TunnelProbeResult(false, "Amnezia adapter without client", Degraded: true);

        if (hasApp)
            return new TunnelProbeResult(false, "Amnezia open but no Up tunnel adapter", Degraded: true);

        return new TunnelProbeResult(false, "Amnezia tunnel not detected");
    }
}
