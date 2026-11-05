namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class ProtonProvider : ITunnelProvider
{
    public string Id => "proton";
    public string DisplayName => "Proton VPN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("ProtonVPN", "Proton VPN", "ProtonVPN.Client");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "Proton", "ProtonVPN");

        if (hasApp && adapter is not null &&
            (adapter.Name.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("Proton", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasApp)
            return new TunnelProbeResult(false, "Proton VPN open but tunnel down", Degraded: true);

        return new TunnelProbeResult(false, "Proton VPN tunnel not detected");
    }
}
