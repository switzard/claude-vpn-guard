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
                case "revokeAllow":
                    _guard.RevokeTemporaryAllow();
                    PushState();
                    break;
                case "applyProfile":
                    if (root.TryGetProperty("id", out var pid) && pid.GetString() is { Length: > 0 } profileId)
                        _guard.ApplyProfile(profileId);
                    PushState();
                    break;
                case "saveProfile":
                {
                    var name = root.TryGetProperty("name", out var pn) ? pn.GetString() : null;
                    _guard.SaveCurrentAsProfile(name ?? "");
                    PushState();
                    break;
                }
                case "deleteProfile":
                    if (root.TryGetProperty("id", out var del) && del.GetString() is { Length: > 0 } delId)
                        _guard.DeleteProfile(delId);
                    PushState();
                    break;
                case "reanchorPassport":
                    _guard.ReanchorPassport();
                    PushState();
                    break;
                case "ackIncident":
                    if (root.TryGetProperty("id", out var iid) && iid.GetString() is { Length: > 0 } incidentId)
                        _guard.AcknowledgeIncident(incidentId);
                    PushState();
                    break;
                case "ackAllIncidents":
                    _guard.AcknowledgeAllIncidents();
                    PushState();
                    break;
                case "showHud":
                    (System.Windows.Application.Current as App)?.ShowHud();
                    break;
                case "audit":
                    _ = _guard.RunAuditAsync();
                    break;
                case "clearEvents":
                    _guard.Journal.ClearAll();
                    PushState();
                    break;
                case "exportJournal":
                    ExportJournal();
                    break;
                case "save":
                    ApplySave(root);
                    // Only force-hide when disabled. Show is explicit via showHud / tray
                    // so ×-hide is not undone by unrelated autoSave.
                    if (System.Windows.Application.Current is App app && !_guard.Settings.HudEnabled)
                        app.HideHud();
                    break;
                case "pickSound":
                    PickSound();
                    break;
                case "previewSound":
                    _guard.PreviewSound();
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
            Filter = "WAV (*.wav)|*.wav",
            Title = "Custom notification sound"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var path = dlg.FileName;
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                new FileInfo(path).Length is <= 0 or >= 8_000_000)
            {
                PushToast("error", "Sound", "Need a non-empty .wav under 8 MB", DateTime.Now.ToString("HH:mm"));
                return;
            }
            var s = _guard.Settings;
            s.CustomSoundPath = path;
            s.SoundPreset = "custom";
            s.SoundEnabled = true;
            _guard.UpdateSettings(s);
            PushToast("success", "Sound", Path.GetFileName(path), DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex)
        {
            PushToast("error", "Sound", ex.Message, DateTime.Now.ToString("HH:mm"));
        }
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
            s.WindowChromeStyle = cs is "win" or "mac" ? cs : "win";
        if (root.TryGetProperty("soundEnabled", out var se)) s.SoundEnabled = se.GetBoolean();
        if (root.TryGetProperty("customSoundPath", out var sp) && sp.GetString() is { } path)
        {
            // Only accept existing .wav under reasonable size — bridge must not read arbitrary paths.
            try
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path) &&
                    new FileInfo(path).Length is > 0 and < 8_000_000)
                    s.CustomSoundPath = path;
            }
            catch { /* ignore bad path */ }
        }
        if (root.TryGetProperty("soundPreset", out var preset) && preset.GetString() is { Length: > 0 } pr)
            s.SoundPreset = pr;
        if (root.TryGetProperty("soundVolume", out var vol) && vol.TryGetInt32(out var v))
            s.SoundVolume = Math.Clamp(v, 0, 100);
        if (root.TryGetProperty("relaunchClaudeOnVpnRestore", out var rl))
            s.RelaunchClaudeOnVpnRestore = rl.GetBoolean();
        if (root.TryGetProperty("preferSingleProvider", out var psp))
            s.PreferSingleProvider = psp.GetBoolean();
        if (root.TryGetProperty("preferToastOnly", out var pto))
            s.PreferToastOnly = pto.GetBoolean();
        if (root.TryGetProperty("hudEnabled", out var hud))
            s.HudEnabled = hud.GetBoolean();
        if (root.TryGetProperty("customTunAdapter", out var cta) && cta.GetString() is { } adapterFrag)
            s.CustomTunAdapterFragment = adapterFrag.Trim();
        if (root.TryGetProperty("customTunProcess", out var ctp) && ctp.GetString() is { } procFrag)
            s.CustomTunProcessFragment = procFrag.Trim();
        if (!string.IsNullOrWhiteSpace(s.CustomSoundPath) &&
            string.Equals(s.SoundPreset, "custom", StringComparison.OrdinalIgnoreCase))
        {
            /* keep custom path */
        }
        else if (string.Equals(s.SoundPreset, "custom", StringComparison.OrdinalIgnoreCase) &&
                 string.IsNullOrWhiteSpace(s.CustomSoundPath))
        {
            s.SoundPreset = "meme";
        }
        if (root.TryGetProperty("providers", out var prov) && prov.ValueKind == JsonValueKind.Array)
        {
            var priority = new List<string>();
            foreach (var p in prov.EnumerateArray())
            {
                var id = p.GetProperty("id").GetString();
                var en = p.GetProperty("enabled").GetBoolean();
                if (id is null) continue;
                s.EnabledProviders[id] = en;
                priority.Add(id);
            }
            if (priority.Count > 0)
                s.ProviderPriority = priority;
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
        var vpnDegraded = t?.State == Tunnel.TunnelState.Degraded;
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

        // Spark = real journal activity per hour over last 12h (not tick deltas).
        var spark = BuildActivitySpark();
        var wave = _guard.ProtectionSpark.Length >= 4
            ? _guard.ProtectionSpark
            : Enumerable.Repeat(vpnUp ? 28.0 : 10.0, 12).ToArray();

        var launches1h = CountEvents("launch", TimeSpan.FromHours(1));
        var suspends1h = CountEvents("suspend", TimeSpan.FromHours(1));
        var suspends24h = CountEvents("suspend", TimeSpan.FromHours(24));
        var blocked24h = CountBlockedLaunches(TimeSpan.FromHours(24));
        var launches = _guard.LaunchAttempts;
        var suspends = _guard.SuspendEvents;
        // Launch blocks (WMI) vs suspends vs total interventions — never collapse into one fake number.
        var blocked = _guard.BlockedStarts;
        var interventions = blocked + suspends;
        var geoOk = g is { Ok: true };
        var locationOk = !_guard.Settings.StrictLocation || _guard.LocationMatchesTarget;
        var pass = _guard.LastPassport;
        UiCopy.SetLang(_guard.Settings);
        var systemSub = UiCopy.SystemSub(
            _guard.Settings.GuardEnabled, vpnUp, vpnDegraded, geoOk, locationOk,
            _guard.Settings.TargetCountryCode);

        var tempMmSs = $"{_guard.TemporaryAllowRemainingSeconds / 60:D2}:{_guard.TemporaryAllowRemainingSeconds % 60:D2}";
        var payload = new
        {
            appVersion = _guard.AppVersion,
            systemTitle = _guard.Settings.GuardEnabled ? UiCopy.SystemActive() : UiCopy.SystemOff(),
            systemSub,
            locationOk,
            globalStatus = !_guard.Settings.GuardEnabled ? UiCopy.GlobalPause()
                : (vpnUp && locationOk ? UiCopy.GlobalActive() : UiCopy.GlobalWait()),
            // Tunnel column always reports VPN — Guard pause lives on system/Claude copy, not here.
            protectionTitle = vpnUp ? UiCopy.ProtectionVpnOk()
                : vpnDegraded ? UiCopy.ProtectionVpnIdle() : UiCopy.ProtectionVpnDown(),
            protectionSub = !_guard.Settings.GuardEnabled
                ? UiCopy.SummaryGuardOff()
                : vpnUp
                    ? (t?.ActiveProviderName ?? UiCopy.TunnelActive())
                    : _guard.StatusSummary,
            uptime = (DateTimeOffset.Now - _guard.StartedAt).ToString(@"hh\:mm\:ss"),
            blockedStarts = blocked,
            launchAttempts = launches,
            suspendEvents = suspends,
            interventions,
            vpnUp,
            vpnDegraded,
            vpnDetail = t?.Detail ?? "",
            vpnCountry = g is { Ok: true }
                ? (g.CountryName ?? g.CountryCode ?? "—")
                : (g?.Error is { Length: > 0 } ? UiCopy.GeoUnavailable() : UiCopy.GeoWaiting()),
            vpnIp = g?.Ip ?? "—",
            vpnLatency = _guard.LastLatencyMs is long ms ? $"{ms} ms" : "—",
            vpnSource = g?.Source ?? "",
            countryCode = code ?? "",
            countries = KnownCountries.All.Select(c => new[] { c.Code, c.Label }).ToArray(),
            claudeAllowed = _guard.ClaudeAllowed,
            claudeSub = !_guard.Settings.GuardEnabled
                ? UiCopy.SummaryGuardOff()
                : _guard.ClaudeAllowed
                    ? (_guard.TemporaryAllowActive
                        ? UiCopy.ClaudeTempAllow(tempMmSs)
                        : UiCopy.ClaudeAllowedOk())
                    : (string.IsNullOrWhiteSpace(_guard.StatusSummary)
                        ? UiCopy.ClaudeBlockedFallback()
                        : _guard.StatusSummary),
            statusSummary = _guard.StatusSummary,
            reminderText = _guard.ReminderText ?? "",
            temporaryAllowSeconds = _guard.TemporaryAllowRemainingSeconds,
            geoAgreement = g is { Ok: true }
                ? $"{g.AgreementCount}/{g.SampleCount}"
                : "—",
            geoSources = g?.Source ?? "",
            tunnelWhy = vpnUp
                ? $"Up · {t?.ActiveProviderName ?? "?"} · egress {(string.IsNullOrWhiteSpace(t?.AdapterName) ? "default" : t!.AdapterName)}"
                : vpnDegraded
                    ? $"Degraded · {t?.Detail ?? "client without tunnel"}"
                    : $"Down · {t?.Detail ?? "no provider"}",
            latencyMode = _guard.LastLatencyMode,
            temporaryAllowActive = _guard.TemporaryAllowActive,
            preferToastOnly = _guard.Settings.PreferToastOnly,
            hudEnabled = _guard.Settings.HudEnabled,
            passport = new
            {
                fingerprint = pass.Fingerprint,
                state = pass.State.ToString(),
                ip = pass.Ip ?? "",
                countryCode = pass.CountryCode ?? "",
                agreement = pass.SampleCount > 0 ? $"{pass.AgreementCount}/{pass.SampleCount}" : "—",
                providerId = pass.ProviderId ?? "",
                updatedAt = pass.UpdatedAt.ToString("HH:mm:ss")
            },
            passportShifts = _guard.Passport.RecentShifts.Select(s => new
            {
                state = s.State.ToString(),
                from = s.FromFingerprint,
                to = s.ToFingerprint,
                reason = s.Reason,
                time = s.Timestamp.ToString("HH:mm · dd.MM")
            }),
            incidents = IncidentStore.Recent(30).Select(i => new
            {
                i.Id,
                i.Open,
                i.Kind,
                i.Summary,
                i.PassportBefore,
                i.PassportAfter,
                i.ProviderName,
                i.AdapterName,
                i.ExitIp,
                i.ExitCountry,
                adapters = i.AdaptersUp,
                processes = i.ClaudeProcesses,
                time = i.Timestamp.ToString("HH:mm · dd.MM"),
                ackAt = i.AcknowledgedAt?.ToString("HH:mm · dd.MM")
            }),
            openIncidentCount = IncidentStore.Recent(40).Count(i => i.Open),
            customTunAdapter = _guard.Settings.CustomTunAdapterFragment,
            customTunProcess = _guard.Settings.CustomTunProcessFragment,
            activeProfileId = _guard.Settings.ActiveProfileId ?? "",
            profiles = _guard.Settings.Profiles.Select(p => new
            {
                p.Id,
                p.Name,
                p.TargetCountryCode,
                p.StrictLocation,
                p.PreferSingleProvider,
                p.KillInsteadOfSuspend,
                Active = string.Equals(p.Id, _guard.Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase)
            }),
            exitHistory = ExitHistoryStore.Recent(24).Select(e => new
            {
                e.Ok,
                e.Ip,
                e.CountryCode,
                e.CountryName,
                e.Source,
                e.Note,
                Time = e.Timestamp.ToString("HH:mm · dd.MM")
            }),
            accountRating = _guard.AccountRating,
            auditOk = _guard.LastAuditOk,
            auditTime = _guard.LastAuditAt?.ToString("HH:mm:ss • dd.MM.yyyy") ?? "—",
            auditDetail = _guard.LastAuditDetail,
            attemptsDelta = $"за 1ч: {launches1h}",
            attemptsDeltaTone = launches1h > 0 ? "up" : "flat",
            // Honest label: count is from in-memory journal window (≤300 / boot ≤80), not a guaranteed 24h ledger.
            blockedDelta = blocked24h > 0 ? $"journal: {blocked24h}" : "journal: 0",
            blockedDeltaTone = blocked24h > 0 ? "up" : "flat",
            suspendDelta = $"за 1ч: {suspends1h}",
            suspendDeltaTone = suspends1h > 0 ? "up" : "flat",
            suspends24h,
            targetCountry = _guard.Settings.TargetCountryCode,
            strictLocation = _guard.Settings.StrictLocation,
            preferSingleProvider = _guard.Settings.PreferSingleProvider,
            providerPriority = _guard.Settings.ResolveProviderOrder(
                GuardService.CreateProviders(() => _guard.Settings).Select(p => p.Id)),
            activeProviderId = t?.ActiveProviderId ?? "",
            guardEnabled = _guard.Settings.GuardEnabled,
            killMode = _guard.Settings.KillInsteadOfSuspend,
            startWithWindows = _guard.Settings.StartWithWindows || AutostartManager.IsEnabled(),
            uiLanguage = _guard.Settings.UiLanguage,
            windowChromeStyle = _guard.Settings.WindowChromeStyle,
            soundEnabled = _guard.Settings.SoundEnabled,
            soundPreset = _guard.Settings.SoundPreset,
            soundVolume = _guard.Settings.SoundVolume,
            relaunchClaudeOnVpnRestore = _guard.Settings.RelaunchClaudeOnVpnRestore,
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
            network = BuildNetworkRows(t, g, vpnUp, vpnDegraded),
            providers = BuildProviderRows(t),
            hygiene = AccountHygiene.Tips.Select(h => new { h.Title, h.Body, h.EnforcedByGuard, h.Unverified }),
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
        // Always prefer journal hour buckets — tick ActivitySpark is relative noise, not a 12h chart.
        var now = DateTimeOffset.Now;
        var buckets = new int[12];
        foreach (var e in _guard.Journal.Entries)
        {
            var ageH = (now - e.Timestamp).TotalHours;
            if (ageH < 0 || ageH >= 12) continue;
            var idx = 11 - (int)Math.Floor(ageH);
            if (idx is < 0 or > 11) continue;
            if (e.Kind is "launch" or "suspend" or "kill" or "vpn" or "geo" or "error" or "audit" or "remind")
                buckets[idx]++;
        }

        // If journal empty, fall back to live activity spark so chart is not a flat zero line.
        if (buckets.All(v => v == 0) && _guard.ActivitySpark.Length >= 4)
            return _guard.ActivitySpark;

        return buckets;
    }

    private int CountEvents(string kind, TimeSpan window) =>
        _guard.Journal.Entries.Count(e =>
            e.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
            e.Timestamp >= DateTimeOffset.Now - window);

    private int CountBlockedLaunches(TimeSpan window) =>
        _guard.Journal.Entries.Count(e =>
            e.Timestamp >= DateTimeOffset.Now - window &&
            e.Kind.Equals("launch", StringComparison.OrdinalIgnoreCase) &&
            (e.Message.Contains("заблокир", StringComparison.OrdinalIgnoreCase) ||
             e.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase)));

    private object[] BuildProviderRows(Tunnel.TunnelStatus? t)
    {
        IEnumerable<Tunnel.ProviderStatus> rows;
        if (t?.Providers is { Count: > 0 } live)
        {
            rows = live;
        }
        else
        {
            var known = GuardService.CreateProviders(() => _guard.Settings)
                .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            rows = _guard.Settings.ResolveProviderOrder(known.Keys)
                .Where(known.ContainsKey)
                .Select(id =>
                {
                    var p = known[id];
                    return new Tunnel.ProviderStatus(
                        p.Id,
                        p.DisplayName,
                        _guard.Settings.IsProviderEnabled(p.Id),
                        false,
                        "—");
                });
        }

        return rows.Select(p => (object)new
        {
            p.Id,
            p.Name,
            p.Enabled,
            p.Healthy,
            p.Degraded,
            Active = string.Equals(t?.ActiveProviderId, p.Id, StringComparison.OrdinalIgnoreCase),
            Status = p.Detail
        }).ToArray();
    }

    private static object[] BuildNetworkRows(Tunnel.TunnelStatus? t, GeoResult? g, bool vpnUp, bool vpnDegraded)
    {
        var tunnelLabel = vpnUp ? "Up" : vpnDegraded ? "Degraded" : "Down";
        var agree = g is { SampleCount: > 0 } ? $"{g.AgreementCount}/{g.SampleCount}" : "—";
        var rows = new List<object>
        {
            new { name = "Tunnel", value = tunnelLabel, ok = vpnUp },
            new { name = "Why", value = vpnUp
                ? $"adapter {t?.AdapterName ?? "—"}"
                : (t?.Detail ?? "—"), ok = vpnUp },
            new { name = "Exit IP", value = g?.Ip ?? "—", ok = g?.Ok == true },
            new { name = "Country", value = g?.CountryCode ?? "—", ok = g?.Ok == true },
            new { name = "Geo agreement", value = agree, ok = g is { Ok: true, AgreementCount: > 1 } },
            new { name = "Geo source", value = g?.Source ?? "—", ok = g?.Ok == true },
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
        kind is "vpn" or "launch" or "geo" or "audit" or "remind" or "error" or "incident";

    private static string ToastType(string kind, string message)
    {
        if (kind is "error" or "launch" or "incident") return "danger";
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
        "incident" => "Incident Freeze Pack",
        _ => "Claude Code Guardian"
    };
}
