using System.Text.Json.Serialization;

namespace ClaudeVpnGuard.Core;

public sealed class AppSettings
{
    public string TargetCountryCode { get; set; } = "NL";
    public string TargetLocationLabel { get; set; } = "Netherlands";
    public bool StrictLocation { get; set; } = true;
    public bool KillInsteadOfSuspend { get; set; } = false;
    public bool GuardEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public int PollIntervalMs { get; set; } = 800;
    public int GeoCheckIntervalSeconds { get; set; } = 45;
    public int ReminderCooldownSeconds { get; set; } = 300;
    public Dictionary<string, bool> EnabledProviders { get; set; } = CreateDefaultProviders();

    public bool IsProviderEnabled(string id) =>
        !EnabledProviders.TryGetValue(id, out var enabled) || enabled;

    public static Dictionary<string, bool> CreateDefaultProviders() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["happ"] = true,
        ["hiddify"] = true,
        ["v2rayn"] = true,
        ["clash-verge"] = true,
        ["amnezia"] = true,
        ["wireguard"] = true,
        ["windscribe"] = true,
        ["outline"] = true,
        ["nekoray"] = true
    };
}

public static class KnownCountries
{
    public static IReadOnlyList<(string Code, string Label)> All { get; } =
    [
        ("NL", "Netherlands"),
        ("DE", "Germany"),
        ("US", "United States"),
        ("GB", "United Kingdom"),
        ("FR", "France"),
        ("FI", "Finland"),
        ("SE", "Sweden"),
        ("CH", "Switzerland"),
        ("PL", "Poland"),
        ("TR", "Turkey"),
        ("JP", "Japan"),
        ("SG", "Singapore"),
        ("CA", "Canada"),
        ("AT", "Austria"),
        ("IE", "Ireland")
    ];
}
