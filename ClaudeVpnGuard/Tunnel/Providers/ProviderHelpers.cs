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

    /// <summary>
    /// Stricter Contains for Custom TUN: fragment must be ≥4 chars and match as a token-ish
    /// substring (rejects tiny frags like "tu"/"et" that hit Ethernet).
    /// </summary>
    public static AdapterInfo? FindUpAdapterExactish(SystemSnapshot snapshot, string fragment)
    {
        var frag = (fragment ?? "").Trim();
        if (frag.Length < 4) return null;
        return snapshot.Adapters.FirstOrDefault(a =>
        {
            if (a.Status != OperationalStatus.Up || IsExcludedAdapter(a)) return false;
            var hay = $"{a.Name} {a.Description}";
            var idx = hay.IndexOf(frag, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            // Prefer boundary: start, end, or non-letter neighbors — cuts "VPN" inside random names less often
            // when fragment is short; long fragments (≥8) still win on plain Contains.
            if (frag.Length >= 8) return true;
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(hay[idx - 1]);
            var after = idx + frag.Length;
            var afterOk = after >= hay.Length || !char.IsLetterOrDigit(hay[after]);
            return beforeOk || afterOk;
        });
    }
}
