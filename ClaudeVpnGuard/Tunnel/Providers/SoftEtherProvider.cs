namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class SoftEtherProvider : ITunnelProvider
{
    public string Id => "softether";
    public string DisplayName => "SoftEther";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains(
            "vpnclient", "vpnclient_x64", "SoftEther", "SEVPNGUI", "vpnsmgr");
        var adapter = ProviderHelpers.FindUpAdapter(
            snapshot,
            "VPN Client Adapter",
            "SoftEther",
            "SE-VPN",
            "VPN Gate");

        if (hasApp && adapter is not null)
            return new TunnelProbeResult(true, $"SoftEther + {adapter.Name}", adapter.Name);

        // SoftEther virtual NIC can stay Up without client UI — still require app/core.
        if (adapter is not null)
            return new TunnelProbeResult(false, "SoftEther adapter without client", Degraded: true);

        if (hasApp)
            return new TunnelProbeResult(false, "SoftEther client open, tunnel down", Degraded: true);

        return new TunnelProbeResult(false, "SoftEther tunnel not detected");
    }
}
