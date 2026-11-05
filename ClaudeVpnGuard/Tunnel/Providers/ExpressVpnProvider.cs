namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class ExpressVpnProvider : ITunnelProvider
{
    public string Id => "expressvpn";
    public string DisplayName => "ExpressVPN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("ExpressVPN", "expressvpn");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "ExpressVPN", "expressvpn", "Lightway");

        if (hasApp && adapter is not null &&
            (adapter.Name.Contains("Express", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("Express", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("Lightway", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        // Lightway Turbo often has no NIC — never claim Up from process alone.
        if (hasApp)
            return new TunnelProbeResult(false, "ExpressVPN open but no tunnel adapter (Turbo?)", Degraded: true);

        return new TunnelProbeResult(false, "ExpressVPN tunnel not detected");
    }
}
