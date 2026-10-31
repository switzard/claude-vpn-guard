using ClaudeVpnGuard.Core;

namespace ClaudeVpnGuard.Tunnel;

public enum TunnelState
{
    Down,
    Up,
    Degraded
}

public sealed class TunnelStatus
{
    public TunnelState State { get; init; }
    public string? ActiveProviderId { get; init; }
    public string? ActiveProviderName { get; init; }
    public string Detail { get; init; } = "";
    public IReadOnlyList<ProviderStatus> Providers { get; init; } = [];
}

public sealed record ProviderStatus(string Id, string Name, bool Enabled, bool Healthy, string Detail);

public sealed class TunnelMonitor
{
    private readonly IReadOnlyList<ITunnelProvider> _providers;
    private readonly Func<AppSettings> _settings;

    public TunnelMonitor(IEnumerable<ITunnelProvider> providers, Func<AppSettings> settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    public TunnelStatus Evaluate(SystemSnapshot? snapshot = null)
    {
        snapshot ??= SystemSnapshot.Capture();
        var settings = _settings();
        var results = new List<ProviderStatus>();
        ProviderStatus? best = null;

        foreach (var provider in _providers)
        {
            var enabled = settings.IsProviderEnabled(provider.Id);
            if (!enabled)
            {
                results.Add(new ProviderStatus(provider.Id, provider.DisplayName, false, false, "disabled"));
                continue;
            }

            var probe = provider.Probe(snapshot);
            var status = new ProviderStatus(provider.Id, provider.DisplayName, true, probe.Healthy, probe.Detail);
            results.Add(status);
            if (probe.Healthy && best is null)
                best = status;
        }

        if (best is null)
        {
            return new TunnelStatus
            {
                State = TunnelState.Down,
                Detail = "No healthy VPN/proxy tunnel detected",
                Providers = results
            };
        }

        return new TunnelStatus
        {
            State = TunnelState.Up,
            ActiveProviderId = best.Id,
            ActiveProviderName = best.Name,
            Detail = best.Detail,
            Providers = results
        };
    }
}
