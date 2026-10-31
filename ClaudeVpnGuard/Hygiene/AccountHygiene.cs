namespace ClaudeVpnGuard.Hygiene;

/// <summary>
/// Community-reported risk factors (unverified). Only network-hygiene tips are actionable in-app.
/// </summary>
public static class AccountHygiene
{
    public static IReadOnlyList<HygieneTip> Tips { get; } =
    [
        new(
            "Keep one exit country",
            "Jumping DE→US→NL in one day is a common community-reported trust signal. Lock a single location in Guard.",
            true),
        new(
            "Never let the tunnel drop mid-session",
            "A brief real-IP flash on an unsupported region is cited as an instant ban trigger. Killswitch covers this.",
            true),
        new(
            "Warm the tunnel before Claude",
            "Start VPN, confirm target country in Guard, then open Claude Code / Desktop.",
            true),
        new(
            "Avoid multi-device thrash",
            "Frequent device switching on one account is reported to lower trust. Prefer one primary PC.",
            false),
        new(
            "Don't burn quotas in a burst",
            "Exhausting daily/weekly limits in a short window is described as bot-like. Pace heavy sessions.",
            false),
        new(
            "Stable email / payment identity",
            "Fresh throwaway mail and mismatched card country vs VPN exit are reported high-risk. Guard cannot fix payment/KYC issues.",
            false),
        new(
            "Usage Policy still applies",
            "Jailbreaks, malware requests, and account sharing are hard bans — no VPN tool changes that.",
            false)
    ];
}

public sealed record HygieneTip(string Title, string Body, bool EnforcedByGuard);
