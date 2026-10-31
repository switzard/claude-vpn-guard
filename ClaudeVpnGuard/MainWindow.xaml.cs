using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ClaudeVpnGuard.Core;
using ClaudeVpnGuard.Hygiene;
using Microsoft.Web.WebView2.Core;

namespace ClaudeVpnGuard;

public partial class MainWindow : Window
{
    private readonly GuardService _guard;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private string? _lastToastKey;
    private bool _ready;
    private IntPtr _hwnd;

    public MainWindow(GuardService guard)
    {
        InitializeComponent();
        _guard = guard;
        Loaded += async (_, _) => await InitWebAsync();
        _guard.StateChanged += OnGuardChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableRoundedCorners(_hwnd);
        // Same DWM minimize/restore family Cursor uses on Windows
        NativeMethods.EnableNativeWindowTransitions(_hwnd);
    }

    private async Task InitWebAsync()
    {
        var uiDir = Path.Combine(AppContext.BaseDirectory, "UI");
        var index = Path.Combine(uiDir, "index.html");
        if (!File.Exists(index))
        {
            System.Windows.MessageBox.Show($"UI not found:\n{index}", "Claude Code Guardian");
            return;
        }

        var env = await CoreWebView2Environment.CreateAsync();
        await WebView.EnsureCoreWebView2Async(env);
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        WebView.CoreWebView2.WebMessageReceived += OnWebMessage;
        WebView.CoreWebView2.Navigate(new Uri(index).AbsoluteUri);
    }

    private void OnGuardChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_ready) PushState();
        });
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() : null;
            switch (cmd)
            {
                case "ready":
                    _ready = true;
                    PushState();
                    break;
                case "checkVpn":
                    _ = _guard.CheckVpnAsync();
                    break;
                case "allowLaunch":
                    _guard.AllowLaunchTemporarily(TimeSpan.FromMinutes(15));
                    break;
                case "audit":
                    _ = _guard.RunAuditAsync();
                    break;
                case "clearEvents":
                    _guard.Journal.ClearMemory();
                    PushState();
                    break;
                case "exportJournal":
                    ExportJournal();
                    break;
                case "save":
                    ApplySave(root);
                    break;
                case "pickSound":
                    PickSound();
                    break;
                case "windowMinimize":
                    if (_hwnd == IntPtr.Zero)
                        _hwnd = new WindowInteropHelper(this).Handle;
                    NativeMethods.MinimizeNative(_hwnd);
                    break;
                case "windowClose":
                    Close();
                    break;
                case "windowDrag":
                    if (_hwnd == IntPtr.Zero)
                        _hwnd = new WindowInteropHelper(this).Handle;
                    NativeMethods.StartWindowDrag(_hwnd);
                    break;
            }
        }
        catch (Exception ex)
        {
            _guard.Journal.Add("error", $"UI message: {ex.Message}");
        }
    }

    private void PickSound()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WAV (*.wav)|*.wav|All files|*.*",
            Title = "Custom notification sound"
        };
        if (dlg.ShowDialog() != true) return;
        var s = _guard.Settings;
        s.CustomSoundPath = dlg.FileName;
        s.SoundEnabled = true;
        _guard.UpdateSettings(s);
        PushToast("success", "Sound", Path.GetFileName(dlg.FileName), DateTime.Now.ToString("HH:mm"));
    }

    private void ApplySave(JsonElement root)
    {
        var s = _guard.Settings;
        if (root.TryGetProperty("targetCountry", out var tc) && tc.GetString() is { Length: > 0 } code)
        {
            s.TargetCountryCode = code;
            var label = KnownCountries.All.FirstOrDefault(x => x.Code == code).Label;
            if (!string.IsNullOrEmpty(label)) s.TargetLocationLabel = label;
        }
        if (root.TryGetProperty("strictLocation", out var sl)) s.StrictLocation = sl.GetBoolean();
        if (root.TryGetProperty("guardEnabled", out var ge)) s.GuardEnabled = ge.GetBoolean();
        if (root.TryGetProperty("killMode", out var km)) s.KillInsteadOfSuspend = km.GetBoolean();
        if (root.TryGetProperty("startWithWindows", out var aw)) s.StartWithWindows = aw.GetBoolean();
        if (root.TryGetProperty("uiLanguage", out var lang) && lang.GetString() is { Length: > 0 } l)
            s.UiLanguage = l;
        if (root.TryGetProperty("windowChromeStyle", out var chrome) && chrome.GetString() is { Length: > 0 } cs)
            s.WindowChromeStyle = cs is "win" or "mac" ? cs : "mac";
        if (root.TryGetProperty("soundEnabled", out var se)) s.SoundEnabled = se.GetBoolean();
        if (root.TryGetProperty("customSoundPath", out var sp) && sp.GetString() is { } path)
            s.CustomSoundPath = path;
        if (root.TryGetProperty("providers", out var prov) && prov.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in prov.EnumerateArray())
            {
                var id = p.GetProperty("id").GetString();
                var en = p.GetProperty("enabled").GetBoolean();
                if (id is not null) s.EnabledProviders[id] = en;
            }
        }
        _guard.UpdateSettings(s);
    }

    private void ExportJournal()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"claude-guardian-journal-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            var src = Path.Combine(SettingsStore.DirectoryPath, "events.jsonl");
            if (File.Exists(src)) File.Copy(src, path, overwrite: true);
            else File.WriteAllText(path, "");
            _guard.Journal.Add("audit", $"Журнал экспортирован: {path}");
            PushToast("success", "Экспорт журнала", "Файл сохранён на рабочий стол", DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex)
        {
            _guard.Journal.Add("error", $"Экспорт: {ex.Message}");
        }
    }

    private void PushState()
    {
        if (WebView.CoreWebView2 is null) return;

        var t = _guard.LastTunnelStatus;
        var g = _guard.LastGeo;
        var vpnUp = t?.State == Tunnel.TunnelState.Up;
        // Never fall back to target country — that made the flag/label lie about the exit.
        var code = g is { Ok: true } ? g.CountryCode : null;

        object? toast = null;
        var latest = _guard.Journal.Entries.FirstOrDefault();
        if (latest is not null)
        {
            var key = $"{latest.Kind}|{latest.Message}|{latest.Timestamp:HH:mm:ss}";
            if (key != _lastToastKey && ShouldToast(latest.Kind))
            {
                _lastToastKey = key;
                toast = new
                {
                    type = ToastType(latest.Kind, latest.Message),
                    title = ToastTitle(latest.Kind, latest.Message),
                    body = latest.Message,
                    time = latest.Timestamp.ToString("HH:mm")
                };
            }
        }

        var spark = BuildActivitySpark();
        var wave = _guard.ProtectionSpark.Length >= 4
            ? _guard.ProtectionSpark
            : Enumerable.Repeat(vpnUp ? 28.0 : 10.0, 12).ToArray();

        var prevHourBlocks = CountEvents("launch", TimeSpan.FromHours(1));
        var prevDayBlocks = CountEvents("launch", TimeSpan.FromHours(24));

        var payload = new
        {
            systemTitle = _guard.Settings.GuardEnabled ? "Система активна" : "Защита выключена",
            systemSub = vpnUp ? "Все защиты работают" : "Ожидание VPN",
            globalStatus = _guard.Settings.GuardEnabled ? "Активен" : "Пауза",
            protectionTitle = !_guard.Settings.GuardEnabled ? "Защита выключена"
                : vpnUp ? "Защита включена" : "Защита активна",
            protectionSub = vpnUp && _guard.ClaudeAllowed
                ? "Все системы работают корректно"
                : _guard.StatusSummary,
            uptime = (DateTimeOffset.Now - _guard.StartedAt).ToString(@"hh\:mm\:ss"),
            blockedStarts = _guard.BlockedStarts,
            launchAttempts = Math.Max(_guard.LaunchAttempts, _guard.BlockedStarts),
            vpnUp,
            vpnCountry = g is { Ok: true }
                ? (g.CountryName ?? g.CountryCode ?? "—")
                : (g?.Error is { Length: > 0 } ? "Geo недоступен" : "Ожидание geo…"),
            vpnIp = g?.Ip ?? "—",
            vpnLatency = _guard.LastLatencyMs is long ms ? $"{ms} ms" : "—",
            countryCode = code ?? "",
            claudeAllowed = _guard.ClaudeAllowed,
            claudeSub = _guard.ClaudeAllowed
                ? (_guard.TemporaryAllowActive
                    ? "Временный доступ активен"
                    : "Запуск разрешён — туннель и локация в норме")
                : "Запуск Claude Code заблокирован до восстановления VPN",
            accountRating = _guard.AccountRating,
            auditOk = _guard.LastAuditOk,
            auditTime = _guard.LastAuditAt?.ToString("HH:mm:ss • dd.MM.yyyy") ?? "—",
            auditDetail = _guard.LastAuditDetail,
            attemptsDelta = prevDayBlocks > 0 ? $"за 24ч: {prevDayBlocks}" : "за 24ч: 0",
            blockedDelta = prevHourBlocks > 0 ? $"за 1ч: {prevHourBlocks}" : "за 1ч: 0",
            targetCountry = _guard.Settings.TargetCountryCode,
            strictLocation = _guard.Settings.StrictLocation,
            guardEnabled = _guard.Settings.GuardEnabled,
            killMode = _guard.Settings.KillInsteadOfSuspend,
            startWithWindows = _guard.Settings.StartWithWindows || AutostartManager.IsEnabled(),
            uiLanguage = _guard.Settings.UiLanguage,
            windowChromeStyle = _guard.Settings.WindowChromeStyle,
            soundEnabled = _guard.Settings.SoundEnabled,
            customSoundPath = string.IsNullOrWhiteSpace(_guard.Settings.CustomSoundPath)
                ? ""
                : Path.GetFileName(_guard.Settings.CustomSoundPath),
            events = _guard.Journal.Entries.Take(40).Select(e => new
            {
                e.Kind,
                e.Message,
                Timestamp = e.Timestamp
            }),
            processes = _guard.ClaudeProcesses.Select(p => new
            {
                Pid = p.Id,
                p.Kind,
                State = p.Suspended ? "приостановлен" : "работает",
                p.Path
            }),
            network = BuildNetworkRows(t, g, vpnUp),
            providers = t?.Providers.Select(p => new
            {
                p.Id,
                p.Name,
                p.Enabled,
                p.Healthy,
                Status = p.Detail
            }) ?? GuardService.CreateProviders().Select(p => new
            {
                Id = p.Id,
                Name = p.DisplayName,
                Enabled = _guard.Settings.IsProviderEnabled(p.Id),
                Healthy = false,
                Status = "—"
            }),
            hygiene = AccountHygiene.Tips.Select(h => new { h.Title, h.Body, h.EnforcedByGuard }),
            recommendations = _guard.BuildRecommendations()
                .Select(r => new { title = r.Title, body = r.Body, tone = r.Tone }),
            spark,
            wave,
            toast
        };

        WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, _json));
    }

    private int[] BuildActivitySpark()
    {
        if (_guard.ActivitySpark.Length >= 4)
            return _guard.ActivitySpark;

        // Build from journal hour buckets (real history)
        var now = DateTimeOffset.Now;
        var buckets = new int[12];
        foreach (var e in _guard.Journal.Entries)
        {
            var ageH = (now - e.Timestamp).TotalHours;
            if (ageH < 0 || ageH >= 12) continue;
            var idx = 11 - (int)ageH;
            if (e.Kind is "launch" or "vpn" or "geo" or "error")
                buckets[idx]++;
        }
        return buckets;
    }

    private int CountEvents(string kind, TimeSpan window) =>
        _guard.Journal.Entries.Count(e =>
            e.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
            e.Timestamp >= DateTimeOffset.Now - window);

    private static object[] BuildNetworkRows(Tunnel.TunnelStatus? t, GeoResult? g, bool vpnUp)
    {
        var rows = new List<object>
        {
            new { name = "Tunnel", value = vpnUp ? "Up" : "Down", ok = vpnUp },
            new { name = "Exit IP", value = g?.Ip ?? "—", ok = g?.Ok == true },
            new { name = "Country", value = g?.CountryCode ?? "—", ok = g?.Ok == true },
            new { name = "Provider", value = t?.ActiveProviderName ?? "—", ok = vpnUp }
        };
        return rows.ToArray();
    }

    private void PushToast(string type, string title, string body, string time)
    {
        if (WebView.CoreWebView2 is null) return;
        var payload = new { toast = new { type, title, body, time } };
        WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, _json));
    }

    private static bool ShouldToast(string kind) =>
        kind is "vpn" or "launch" or "geo" or "audit" or "remind" or "error";

    private static string ToastType(string kind, string message)
    {
        if (kind is "error" or "launch") return "danger";
        if (kind is "vpn" && message.Contains("отключ", StringComparison.OrdinalIgnoreCase)) return "danger";
        if (kind is "vpn" or "audit" or "boot") return "success";
        return "info";
    }

    private static string ToastTitle(string kind, string message) => kind switch
    {
        "launch" => "Попытка запуска Claude Code",
        "vpn" when message.Contains("отключ", StringComparison.OrdinalIgnoreCase) => "VPN отключён",
        "vpn" => "VPN подключен",
        "geo" => "Смена IP",
        "audit" => "Аудит",
        "remind" => "Напоминание о локации",
        _ => "Claude Code Guardian"
    };
}
