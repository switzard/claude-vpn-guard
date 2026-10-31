namespace ClaudeVpnGuard.Tunnel;

public interface ITunnelProvider
{
    string Id { get; }
    string DisplayName { get; }
    TunnelProbeResult Probe(SystemSnapshot snapshot);
}

public sealed record TunnelProbeResult(
    bool Healthy,
    string Detail,
    string? AdapterName = null);
