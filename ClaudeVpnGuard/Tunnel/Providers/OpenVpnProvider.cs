namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class OpenVpnProvider : ITunnelProvider
{
    public string Id => "openvpn";
    public string DisplayName => "OpenVPN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("openvpn", "OpenVPNConnect", "ovpnhelper");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "OpenVPN", "TAP-Windows", "ovpn");

        if (hasApp && adapter is not null &&
            (adapter.Name.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
             adapter.Name.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(true, $"OpenVPN + {adapter.Name}", adapter.Name);

        if (hasApp)
            return new TunnelProbeResult(false, "OpenVPN process without Up adapter", Degraded: true);

        return new TunnelProbeResult(false, "OpenVPN tunnel not detected");
    }
}
