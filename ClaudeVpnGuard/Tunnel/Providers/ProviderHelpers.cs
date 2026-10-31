using System.Net.NetworkInformation;

namespace ClaudeVpnGuard.Tunnel.Providers;

internal static class ProviderHelpers
{
    private static readonly string[] ExcludedAdapterFragments =
    [
        "radmin",
        "hyper-v",
        "vethernet",
        "virtualbox",
        "vmware",
        "loopback",
        "bluetooth",
        "npcap"
    ];

    public static bool IsExcludedAdapter(AdapterInfo adapter)
    {
        var hay = $"{adapter.Name} {adapter.Description}";
        return ExcludedAdapterFragments.Any(f =>
            hay.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasUpAdapter(SystemSnapshot snapshot, params string[] fragments)
    {
        return snapshot.Adapters.Any(a =>
            a.Status == OperationalStatus.Up &&
            !IsExcludedAdapter(a) &&
            fragments.Any(f =>
                a.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    public static AdapterInfo? FindUpAdapter(SystemSnapshot snapshot, params string[] fragments)
    {
        return snapshot.Adapters.FirstOrDefault(a =>
            a.Status == OperationalStatus.Up &&
            !IsExcludedAdapter(a) &&
            fragments.Any(f =>
                a.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
}
