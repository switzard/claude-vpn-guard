namespace ClaudeVpnGuard.Core;

/// <summary>Host-side status / reminder strings keyed by UiLanguage (ru|en|zh).</summary>
internal static class UiCopy
{
    public static string Lang(AppSettings s) =>
        s.UiLanguage?.ToLowerInvariant() switch
        {
            "en" => "en",
            "zh" => "zh",
            _ => "ru"
        };

    public static string ReminderVpn(AppSettings s)
    {
        var loc = s.TargetLocationLabel;
        return Lang(s) switch
        {
            "en" => $"Turn on VPN → {loc}",
            "zh" => $"请开启 VPN → {loc}",
            _ => $"Включите VPN → {loc}"
        };
    }

    public static string ReminderVpnAndLoc(AppSettings s)
    {
        var loc = s.TargetLocationLabel;
        var code = s.TargetCountryCode;
        return Lang(s) switch
        {
            "en" => $"Turn on VPN and set exit to {loc} ({code}).",
            "zh" => $"请开启 VPN 并将出口设为 {loc} ({code})。",
            _ => $"Включите VPN и выберите локацию {loc} ({code})."
        };
    }

    public static string ReminderWrongExit(AppSettings s, string? current)
    {
        var loc = s.TargetLocationLabel;
        var cur = current ?? "?";
        return Lang(s) switch
        {
            "en" => $"VPN is up, but exit is {cur}. Switch node to {loc}.",
            "zh" => $"VPN 已连接，但出口为 {cur}。请切换到 {loc}。",
            _ => $"VPN активен, но выход {cur}. Переключите ноду на {loc}."
        };
    }

    /// <summary>Tunnel is Up but geo samples disagree / failed — do not say "turn on VPN".</summary>
    public static string ReminderGeoUnreliable(AppSettings s)
    {
        var code = s.TargetCountryCode;
        return Lang(s) switch
        {
            "en" => $"Tunnel is up, but exit country is unverified. Wait for geo or switch node toward {code}.",
            "zh" => $"隧道已连接，但出口地区未确认。请等待 geo 或切换节点至 {code}。",
            _ => $"Туннель Up, но exit-страна не подтверждена. Подождите geo или смените ноду на {code}."
        };
    }

    public static string SummaryTempAllow() => Pick("Временный доступ к Claude", "Temporary Claude access", "临时允许 Claude");
    public static string SummaryGuardOff() => Pick("Защита выключена", "Guard paused", "防护已关闭");
    public static string SummaryDegraded() => Pick(
        "VPN-клиент открыт, туннель выключен — Claude заблокирован",
        "VPN client open, tunnel down — Claude blocked",
        "VPN 客户端已开但隧道断开 — Claude 已拦截");
    public static string SummaryVpnDown() => Pick(
        "VPN выключен — Claude заблокирован",
        "VPN off — Claude blocked",
        "VPN 已关闭 — Claude 已拦截");
    public static string SummaryWrongLoc(string? current, string target) =>
        Pick(
            $"Неверная локация ({current ?? "?"}) — нужна {target}",
            $"Wrong exit ({current ?? "?"}) — need {target}",
            $"出口错误（{current ?? "?"}）— 需要 {target}");
    public static string SummaryOk(string? provider, string? country) =>
        Pick(
            $"Защита активна — {provider} / {country ?? "…"}",
            $"Guard active — {provider} / {country ?? "…"}",
            $"防护运行中 — {provider} / {country ?? "…"}");
    public static string SummaryWait() => Pick(
        "Ожидание туннеля и локации",
        "Waiting for tunnel and location",
        "等待隧道与地区");

    public static string SystemActive() => Pick("Система активна", "System active", "系统运行中");
    public static string SystemOff() => Pick("Защита выключена", "Guard paused", "防护已关闭");
    public static string GlobalActive() => Pick("Активен", "Active", "运行中");
    public static string GlobalWait() => Pick("Ожидание", "Waiting", "等待");
    public static string GlobalPause() => Pick("Пауза", "Paused", "暂停");
    public static string ProtectionPause() => Pick("Пауза", "Paused", "暂停");
    public static string ProtectionVpnOk() => "VPN OK";
    public static string ProtectionVpnIdle() => Pick("VPN idle", "VPN idle", "VPN 空闲");
    public static string ProtectionVpnDown() => Pick("VPN down", "VPN down", "VPN 断开");
    public static string TunnelActive() => Pick("туннель активен", "tunnel up", "隧道已连接");
    public static string GeoUnavailable() => Pick("Geo недоступен", "Geo unavailable", "无法获取地区");
    public static string GeoWaiting() => Pick("Ожидание geo…", "Waiting for geo…", "等待地区…");
    public static string ClaudeAllowedOk() => Pick(
        "Запуск разрешён — туннель и локация в норме",
        "Launch allowed — tunnel and location OK",
        "允许启动 — 隧道与地区正常");
    public static string ClaudeTempAllow(string mmss) => Pick(
        $"Временный доступ · {mmss}",
        $"Temporary access · {mmss}",
        $"临时访问 · {mmss}");
    public static string ClaudeBlockedFallback() => Pick(
        "Запуск Claude Code заблокирован",
        "Claude Code launch blocked",
        "Claude Code 启动已拦截");

    public static string SystemSub(bool guardOn, bool vpnUp, bool vpnDegraded, bool geoOk, bool locationOk, string targetCode)
    {
        if (!guardOn) return SystemOff();
        if (!vpnUp && vpnDegraded)
            return Pick("VPN-клиент без туннеля", "VPN client without tunnel", "VPN 客户端无隧道");
        if (!vpnUp) return Pick("Ожидание VPN", "Waiting for VPN", "等待 VPN");
        if (!geoOk) return Pick("Туннель OK · geo ждёт", "Tunnel OK · geo pending", "隧道正常 · 等待地区");
        if (!locationOk)
            return Pick($"Туннель OK · нужна {targetCode}", $"Tunnel OK · need {targetCode}", $"隧道正常 · 需要 {targetCode}");
        return Pick("Туннель + локация OK", "Tunnel + location OK", "隧道与地区正常");
    }

    private static string _lang = "ru";

    public static void SetLang(AppSettings s) => _lang = Lang(s);

    private static string Pick(string ru, string en, string zh) =>
        _lang switch { "en" => en, "zh" => zh, _ => ru };
}
