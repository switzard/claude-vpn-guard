namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class SurfsharkProvider : ITunnelProvider
{
    public string Id => "surfshark";
    public string DisplayName => "Surfshark";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("Surfshark", "surfshark");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "Surfshark", "sshwireguard", "surfshark");

        if (hasApp && adapter is not null &&
            (adapter.Name.Contains("Surfshark", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("Surfshark", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasApp)
            return new TunnelProbeResult(false, "Surfshark open but tunnel down", Degraded: true);

        return new TunnelProbeResult(false, "Surfshark tunnel not detected");
    }
}
