namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class HiddifyProvider : ITunnelProvider
{
    public string Id => "hiddify";
    public string DisplayName => "Hiddify";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("Hiddify");
        var hasCore = snapshot.HasProcess("sing-box", "singbox");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "hiddify", "sftoolkit");

        // sing-box NIC only with Hiddify core present — idle GUI + foreign TUN must not Healthy.
        if (adapter is null && hasApp && hasCore)
            adapter = ProviderHelpers.FindUpAdapter(snapshot, "sing-box", "singbox");

        if (hasApp && hasCore && adapter is not null)
            return new TunnelProbeResult(true, $"Hiddify + {adapter.Name}", adapter.Name);
        if (hasApp && adapter is not null &&
            (adapter.Name.Contains("hiddify", StringComparison.OrdinalIgnoreCase) ||
             adapter.Description.Contains("hiddify", StringComparison.OrdinalIgnoreCase) ||
             adapter.Name.Contains("sftoolkit", StringComparison.OrdinalIgnoreCase)))
            return new TunnelProbeResult(true, $"Hiddify + {adapter.Name}", adapter.Name);

        if (hasApp || hasCore)
            return new TunnelProbeResult(false, "Hiddify open but tunnel adapter down", Degraded: true);

        return new TunnelProbeResult(false, "Hiddify tunnel not detected");
    }
}
