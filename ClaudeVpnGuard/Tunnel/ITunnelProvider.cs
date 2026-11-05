namespace ClaudeVpnGuard.Tunnel;

public interface ITunnelProvider
{
    string Id { get; }
    string DisplayName { get; }
    TunnelProbeResult Probe(SystemSnapshot snapshot);
}

/// <summary>
/// Healthy = live tunnel (adapter Up + client/core as required).
/// Degraded = client/process present but tunnel not connected — must NOT count as VPN ON.
/// </summary>
public sealed record TunnelProbeResult(
    bool Healthy,
    string Detail,
    string? AdapterName = null,
    bool Degraded = false);
