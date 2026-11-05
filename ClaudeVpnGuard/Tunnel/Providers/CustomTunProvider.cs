using ClaudeVpnGuard.Core;

namespace ClaudeVpnGuard.Tunnel.Providers;

/// <summary>User-defined TUN: adapter fragment (+ optional process) from settings.</summary>
public sealed class CustomTunProvider : ITunnelProvider
{
    private readonly Func<AppSettings> _settings;
    private const int MinAdapterFrag = 4;
    private const int MinProcessFrag = 3;

    public CustomTunProvider(Func<AppSettings> settings) => _settings = settings;

    public string Id => "custom";
    public string DisplayName => "Custom TUN";

    public TunnelProbeResult Probe(SystemSnapshot snapshot)
    {
        var s = _settings();
        var frag = (s.CustomTunAdapterFragment ?? "").Trim();
        if (frag.Length < MinAdapterFrag)
            return new TunnelProbeResult(false,
                frag.Length == 0
                    ? "Custom TUN not configured"
                    : $"Custom adapter fragment too short (min {MinAdapterFrag})");

        var adapter = ProviderHelpers.FindUpAdapterExactish(snapshot, frag);
        var procFrag = (s.CustomTunProcessFragment ?? "").Trim();
        var hasProc = true;
        if (procFrag.Length > 0)
        {
            if (procFrag.Length < MinProcessFrag)
                return new TunnelProbeResult(false, $"Custom process fragment too short (min {MinProcessFrag})");
            hasProc = snapshot.HasProcessContains(procFrag);
        }

        if (adapter is not null && hasProc)
            return new TunnelProbeResult(true, $"custom {adapter.Name}", adapter.Name);

        if (adapter is not null)
            return new TunnelProbeResult(false, "Custom adapter up, process missing", Degraded: true);

        if (hasProc && procFrag.Length > 0)
            return new TunnelProbeResult(false, "Custom process open, no matching adapter", Degraded: true);

        return new TunnelProbeResult(false, "Custom TUN not detected");
    }
}
