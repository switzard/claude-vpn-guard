namespace ClaudeVpnGuard.Core;

/// <summary>Named killswitch preset — country, mode, provider pin, enforce style.</summary>
public sealed class KillswitchProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Profile";
    public string TargetCountryCode { get; set; } = "NL";
    public bool StrictLocation { get; set; } = true;
    public bool PreferSingleProvider { get; set; } = false;
    public string? PreferredProviderId { get; set; }
    public bool KillInsteadOfSuspend { get; set; } = false;
    public List<string> ProviderPriority { get; set; } = [];
}
