namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class HiddifyProvider : ITunnelProvider
{
    public string Id => "hiddify";
    public string DisplayName => "Hiddify";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("Hiddify");
        var hasCore = snapshot.HasProcess("sing-box", "singbox");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "hiddify", "sing-box", "wintun", "sftoolkit");

        if (hasApp && (hasCore || adapter is not null))
            return new TunnelProbeResult(true, adapter is null ? "Hiddify + sing-box" : $"Hiddify + {adapter.Name}", adapter?.Name);

        if (hasApp && hasCore)
            return new TunnelProbeResult(true, "Hiddify running with sing-box");

        return new TunnelProbeResult(false, "Hiddify tunnel not detected");
    }
}
