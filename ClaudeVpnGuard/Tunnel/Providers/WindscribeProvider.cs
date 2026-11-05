namespace ClaudeVpnGuard.Tunnel.Providers;

public sealed class WindscribeProvider : ITunnelProvider
{
    public string Id => "windscribe";
    public string DisplayName => "Windscribe";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var hasApp = snapshot.HasProcessContains("Windscribe");
        var adapter = ProviderHelpers.FindUpAdapter(snapshot, "Windscribe", "WindscribeWind");

        if (hasApp && adapter is not null)
            return new TunnelProbeResult(true, $"adapter {adapter.Name}", adapter.Name);

        if (hasApp)
            return new TunnelProbeResult(false, "Windscribe open but no tunnel adapter", Degraded: true);

        return new TunnelProbeResult(false, "Windscribe tunnel not detected");
    }
}
