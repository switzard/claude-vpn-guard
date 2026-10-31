namespace ClaudeVpnGuard.Hygiene;

/// <summary>
/// Community-reported risk factors (unverified). Only network-hygiene tips are actionable in-app.
/// </summary>
public static class AccountHygiene
{
    public static IReadOnlyList<HygieneTip> Tips { get; } =
    [
        new("Используйте VPN при запуске Claude", "Не открывайте Claude Code / Desktop без живого туннеля.", true),
        new("Избегайте частой смены IP", "Прыжки по странам за день — частый community-сигнал снижения доверия.", true),
        new("Не используйте только Linux + веб", "По наблюдениям сообществ, мобильные/Desktop клиенты выглядят «спокойнее».", false),
        new("Не выжигайте лимиты за один сеанс", "Полный burn дневной/недельной квоты за пару часов выглядит ботоподобно.", false),
        new("Один стабильный ПК", "Постоянные входы с разных устройств режут trust-рейтинг.", false),
        new("Usage Policy всё ещё действует", "Джейлбрейки и вредоносные промпты — прямые баны. Guard это не обходит.", false)
    ];

    public static IReadOnlyList<string> Checklist { get; } =
    [
        "Используйте VPN при запуске Claude",
        "Избегайте частой смены IP",
        "Не используйте Linux (веб-версия)",
        "Не выжигайте лимиты за один сеанс"
    ];
}

public sealed record HygieneTip(string Title, string Body, bool EnforcedByGuard);
