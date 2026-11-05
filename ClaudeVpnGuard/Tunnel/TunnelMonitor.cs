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
    public string? AdapterName { get; init; }
    public string Detail { get; init; } = "";
    public IReadOnlyList<ProviderStatus> Providers { get; init; } = [];
}

public sealed record ProviderStatus(
    string Id,
    string Name,
    bool Enabled,
    bool Healthy,
    string Detail,
    bool Degraded = false,
    string? AdapterName = null);

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
        ProviderStatus? connected = null;
        ProviderStatus? degraded = null;

        var byId = _providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var order = settings.ResolveProviderOrder(byId.Keys);
        var preferredId = order.FirstOrDefault(id => settings.IsProviderEnabled(id));

        foreach (var id in order)
        {
            if (!byId.TryGetValue(id, out var provider))
                continue;

            var enabled = settings.IsProviderEnabled(provider.Id);
            if (!enabled)
            {
                results.Add(new ProviderStatus(provider.Id, provider.DisplayName, false, false, "disabled"));
                continue;
            }

            var probe = provider.Probe(snapshot);
            var status = new ProviderStatus(
                provider.Id,
                provider.DisplayName,
                true,
                probe.Healthy,
                probe.Detail,
                probe.Degraded,
                probe.AdapterName);
            results.Add(status);

            if (probe.Healthy && connected is null)
                connected = status;
            else if (!probe.Healthy && probe.Degraded && degraded is null)
                degraded = status;
        }

        // Pin mode: only the top enabled provider may report Up.
        if (settings.PreferSingleProvider &&
            preferredId is not null &&
            connected is not null &&
            !connected.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase))
        {
            var preferredRow = results.FirstOrDefault(r =>
                r.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
            var detail = preferredRow is null
                ? $"ожидается {preferredId}, найден {connected.Name}"
                : $"{preferredRow.Name}: {preferredRow.Detail} (пин: игнор {connected.Name})";
            return new TunnelStatus
            {
                State = preferredRow is { Degraded: true } ? TunnelState.Degraded : TunnelState.Down,
                ActiveProviderId = preferredId,
                ActiveProviderName = preferredRow?.Name ?? preferredId,
                AdapterName = preferredRow?.AdapterName,
                Detail = detail,
                Providers = results
            };
        }

        if (connected is not null)
        {
            return new TunnelStatus
            {
                State = TunnelState.Up,
                ActiveProviderId = connected.Id,
                ActiveProviderName = connected.Name,
                AdapterName = connected.AdapterName,
                Detail = connected.Detail,
                Providers = results
            };
        }

        if (degraded is not null)
        {
            return new TunnelStatus
            {
                State = TunnelState.Degraded,
                ActiveProviderId = degraded.Id,
                ActiveProviderName = degraded.Name,
                AdapterName = degraded.AdapterName,
                Detail = degraded.Detail,
                Providers = results
            };
        }

        return new TunnelStatus
        {
            State = TunnelState.Down,
            Detail = "No healthy VPN/proxy tunnel detected",
            Providers = results
        };
    }
}
