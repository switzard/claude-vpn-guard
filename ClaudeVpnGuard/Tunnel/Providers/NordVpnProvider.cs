namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class NordVpnProvider : ITunnelProvider
{
    public string Id => "nordvpn";
    public string DisplayName => "NordVPN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("NordVPN", "NordSec", "nordvpn");
        // Avoid bare "Nord" — matches unrelated adapters. Prefer NordLynx / NordTunnel.
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "NordLynx", "NordTunnel", "nordlynx", "NordVPN Tunnel");

        if (hasApp && adapter is not null)
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasApp)
            return new TunnelProbeResult(false, "NordVPN open but tunnel down", Degraded: true);

        return new TunnelProbeResult(false, "NordVPN tunnel not detected");
    }
}
