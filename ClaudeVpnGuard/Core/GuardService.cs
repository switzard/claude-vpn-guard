using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ClaudeVpnGuard.Tunnel;
using ClaudeVpnGuard.Tunnel.Providers;

namespace ClaudeVpnGuard.Core;

public sealed class GuardService : IDisposable
{
    private readonly TunnelMonitor _tunnel;
    private readonly ProcessGuard _processes;
    private readonly LocationWatch _location = new();
    private readonly SessionPassport _passport = new();
    private readonly EventJournal _journal;
    private readonly Notifier _notifier;
    private readonly object _gate = new();
    private bool _hadClaudeWhileUp;
    private bool _locationWasOk;

    private AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TunnelState _lastTunnel = TunnelState.Down;
    private TunnelState _stableTunnel = TunnelState.Down;
    private int _upVotes;
    private int _downVotes;
    private string? _lastCountry;
    private DateTimeOffset _lastGeo = DateTimeOffset.MinValue;
    private DateTimeOffset? _temporaryAllowUntil;
    private DateTimeOffset? _temporaryAllowStartedAt;
    private bool? _wmiAvailableSticky;
    private int _egressFailVotes;
    public string LastLatencyMode { get; private set; } = "icmp-default";
    private int _blockedStarts;
    private int _launchAttempts;
    private int _suspendEvents;
    private long? _lastLatencyMs;
    private readonly Queue<int> _activitySpark = new();
    private readonly Queue<double> _protectionSpark = new();
    private int _prevBlocked;
    private int _prevAttempts;

    public event Action? StateChanged;

    public GuardService(EventJournal journal, Notifier notifier)
    {
        _journal = journal;
        _notifier = notifier;
        _settings = SettingsStore.Load();
        var stats = StatsStore.Load();
        _blockedStarts = stats.BlockedStarts;
        _launchAttempts = stats.LaunchAttempts;
        _suspendEvents = stats.SuspendEvents;
        RestoreActivitySpark(stats);
        _tunnel = new TunnelMonitor(CreateProviders(() => _settings), () => _settings);
        _processes = new ProcessGuard(journal);
        StartedAt = DateTimeOffset.Now;
        LastAuditAt = null;
        LastAuditOk = false;
    }

    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? LastAuditAt { get; private set; }
    public bool LastAuditOk { get; private set; }
    public string LastAuditDetail { get; private set; } = "Аудит ещё не запускался";
    public int BlockedStarts => _blockedStarts;
    public int LaunchAttempts => _launchAttempts;
    public int SuspendEvents => _suspendEvents;
    public long? LastLatencyMs => _lastLatencyMs;
    public AppSettings Settings => _settings;
    public EventJournal Journal => _journal;
    public TunnelStatus? LastTunnelStatus { get; private set; }
    public GeoResult? LastGeo => _location.LastResult;
    public SessionPassport Passport => _passport;
    public PassportSnapshot LastPassport => _passport.Current;
    public IReadOnlyList<ClaudeProcessInfo> ClaudeProcesses { get; private set; } = [];
    public bool ClaudeAllowed { get; private set; }
    public bool LocationMatchesTarget => _location.MatchesTarget(_settings);
    public bool TemporaryAllowActive =>
        _temporaryAllowUntil is { } until && until > DateTimeOffset.Now;
    public int TemporaryAllowRemainingSeconds =>
        TemporaryAllowActive && _temporaryAllowUntil is { } until
            ? Math.Max(0, (int)(until - DateTimeOffset.Now).TotalSeconds)
            : 0;
    public string StatusSummary { get; private set; } = "Запуск…";
    public string ReminderText { get; private set; } = "";
    public int AccountRating { get; private set; } = 70;
    public int[] ActivitySpark => _activitySpark.ToArray();
    public double[] ProtectionSpark => _protectionSpark.ToArray();
    public bool WmiAvailable { get; private set; } = true;
    public string AppVersion =>
        typeof(GuardService).Assembly.GetName().Version?.ToString(3) ?? "2.2.0";

    public static IEnumerable<ITunnelProvider> CreateProviders(Func<AppSettings>? settings = null)
    {
        settings ??= static () => new AppSettings();
        return
        [
            new HappProvider(),
            new HiddifyProvider(),
            new V2rayNProvider(),
            new ClashVergeProvider(),
            new AmneziaProvider(),
            new WireGuardProvider(),
            new WindscribeProvider(),
            new OutlineProvider(),
            new NekorayProvider(),
            new ProtonProvider(),
            new OpenVpnProvider(),
            new SurfsharkProvider(),
            new NordVpnProvider(),
            new ExpressVpnProvider(),
            new ShadowsocksProvider(),
            new SoftEtherProvider(),
            new CustomTunProvider(settings)
        ];
    }

    public void ApplyProfile(string profileId)
    {
        var p = _settings.Profiles.FirstOrDefault(x =>
            x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (p is null) return;
        _settings.ActiveProfileId = p.Id;
        _settings.TargetCountryCode = p.TargetCountryCode;
        var label = KnownCountries.All.FirstOrDefault(c => c.Code == p.TargetCountryCode).Label;
        if (!string.IsNullOrEmpty(label)) _settings.TargetLocationLabel = label;
        _settings.StrictLocation = p.StrictLocation;
        _settings.PreferSingleProvider = p.PreferSingleProvider;
        _settings.KillInsteadOfSuspend = p.KillInsteadOfSuspend;
        if (p.ProviderPriority.Count > 0)
            _settings.ProviderPriority = p.ProviderPriority.ToList();
        else if (!string.IsNullOrWhiteSpace(p.PreferredProviderId))
        {
            var rest = _settings.ProviderPriority
                .Where(id => !id.Equals(p.PreferredProviderId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            rest.Insert(0, p.PreferredProviderId!);
            _settings.ProviderPriority = rest;
        }
        SettingsStore.Save(_settings);
        _journal.Add("settings", $"Профиль «{p.Name}» активирован");
        StateChanged?.Invoke();
    }

    public KillswitchProfile SaveCurrentAsProfile(string name)
    {
        var p = new KillswitchProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Profile {_settings.Profiles.Count + 1}" : name.Trim(),
            TargetCountryCode = _settings.TargetCountryCode,
            StrictLocation = _settings.StrictLocation,
            PreferSingleProvider = _settings.PreferSingleProvider,
            KillInsteadOfSuspend = _settings.KillInsteadOfSuspend,
            PreferredProviderId = _settings.ProviderPriority.FirstOrDefault(),
            ProviderPriority = _settings.ProviderPriority.ToList()
        };
        _settings.Profiles.Add(p);
        _settings.ActiveProfileId = p.Id;
        SettingsStore.Save(_settings);
        _journal.Add("settings", $"Сохранён профиль «{p.Name}»");
        StateChanged?.Invoke();
        return p;
    }

    public void DeleteProfile(string profileId)
    {
        var wasActive = string.Equals(_settings.ActiveProfileId, profileId, StringComparison.OrdinalIgnoreCase);
        var n = _settings.Profiles.RemoveAll(x =>
            x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (n == 0) return;
        if (wasActive)
        {
            var next = _settings.Profiles.FirstOrDefault();
            if (next is not null)
            {
                ApplyProfile(next.Id);
                return;
            }
            _settings.ActiveProfileId = null;
        }
        SettingsStore.Save(_settings);
        _journal.Add("settings", "Профиль удалён");
        StateChanged?.Invoke();
    }

    public void SetGuardPaused(bool paused)
    {
        _settings.GuardEnabled = !paused;
        SettingsStore.Save(_settings);
        _journal.Add("settings", paused ? "Guard на паузе (tray)" : "Guard возобновлён (tray)");
        StateChanged?.Invoke();
    }

    public void ReanchorPassport()
    {
        _passport.Reanchor();
        _journal.Add("passport", "Session Passport: новый якорь (reanchor)");
        StateChanged?.Invoke();
    }

    public bool AcknowledgeIncident(string id)
    {
        var ok = IncidentStore.Acknowledge(id);
        if (ok)
        {
            _journal.Add("incident", $"Инцидент {id} подтверждён");
            StateChanged?.Invoke();
        }
        return ok;
    }

    public int AcknowledgeAllIncidents()
    {
        var n = IncidentStore.AcknowledgeAll();
        if (n > 0)
        {
            _journal.Add("incident", $"Подтверждено инцидентов: {n}");
            StateChanged?.Invoke();
        }
        return n;
    }

    public void Start()
    {
        AutostartManager.SetEnabled(_settings.StartWithWindows);
        _processes.StartWatching();
        _processes.Blocked += OnBlockedLaunch;
        _processes.DetectedStart += OnDetectedStart;
        _processes.Suspended += OnSuspended;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _journal.Add("boot", $"Claude Code Guardian {AppVersion} запущен");
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            SettingsStore.Save(settings);
            AutostartManager.SetEnabled(settings.StartWithWindows);
        }
        _journal.Add("settings", $"Сохранено. Локация={settings.TargetCountryCode}, strict={settings.StrictLocation}");
        StateChanged?.Invoke();
    }

    /// <summary>Max wall-clock for stacked temp allows (refresh cannot exceed this from first grant).</summary>
    private static readonly TimeSpan TempAllowWallClockCap = TimeSpan.FromMinutes(45);

    public void AllowLaunchTemporarily(TimeSpan duration)
    {
        var slice = duration <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : duration;
        if (slice > TimeSpan.FromMinutes(15))
            slice = TimeSpan.FromMinutes(15);

        var now = DateTimeOffset.Now;
        _temporaryAllowStartedAt ??= now;
        var wallDeadline = _temporaryAllowStartedAt.Value + TempAllowWallClockCap;
        var next = now.Add(slice);
        if (next > wallDeadline)
            next = wallDeadline;
        if (next <= now)
        {
            _temporaryAllowUntil = null;
            _temporaryAllowStartedAt = null;
            _journal.Add("override", "Временный доступ исчерпан (лимит 45 мин wall-clock)");
            // Cap exhaust must close WMI allow immediately — do not wait for next tick publish.
            _processes.SetAllowed(false);
            _processes.Enforce(false, _settings.KillInsteadOfSuspend);
            ClaudeAllowed = false;
            StateChanged?.Invoke();
            return;
        }

        _temporaryAllowUntil = next;
        var left = (int)Math.Ceiling((_temporaryAllowUntil.Value - now).TotalMinutes);
        _journal.Add("override", $"Временный запуск Claude разрешён · ещё ~{left} мин (cap 45)");
        _notifier.Balloon("Временный доступ", $"Claude разрешён · ещё ~{left} мин", ToolTipIcon.Info);
        // Optimistic allow for this tick window
        _processes.SetAllowed(true);
        StateChanged?.Invoke();
    }

    public void RevokeTemporaryAllow()
    {
        _temporaryAllowUntil = null;
        _temporaryAllowStartedAt = null;
        _journal.Add("override", "Временный доступ отозван");
        _processes.SetAllowed(false);
        _processes.Enforce(false, _settings.KillInsteadOfSuspend);
        ClaudeAllowed = false;
        StateChanged?.Invoke();
    }

    public void PreviewSound() => _notifier.PreviewSound();

    public async Task CheckVpnAsync()
    {
        var sw = Stopwatch.StartNew();
        var geo = await _location.RefreshAsync();
        sw.Stop();
        var pingMs = await ProbeLatencyAsync();
        _lastLatencyMs = pingMs ?? sw.ElapsedMilliseconds;
        _lastGeo = DateTimeOffset.Now;
        if (geo.Ok)
        {
            var src = string.IsNullOrWhiteSpace(geo.Source) ? "" : $" · {geo.Source}";
            var lat = pingMs is long p ? $"{p} мс egress" : $"{sw.ElapsedMilliseconds} мс geo";
            _journal.Add("vpn", $"Проверка VPN: {geo.CountryName} ({geo.CountryCode}) · {geo.Ip}{src} · {lat}");
            _notifier.Balloon("VPN проверен", $"{geo.CountryName} · {geo.Ip} · {lat}", ToolTipIcon.Info);
            RecordExit(geo, "manual-check");
        }
        else
        {
            _journal.Add("vpn", $"Проверка VPN не удалась: {geo.Error}");
            _notifier.Balloon("VPN", "Не удалось получить exit IP", ToolTipIcon.Warning);
        }
        StateChanged?.Invoke();
    }

    public async Task RunAuditAsync()
    {
        var issues = new List<string>();
        var snapshot = SystemSnapshot.Capture();
        var raw = _tunnel.Evaluate(snapshot);
        if (raw.State == TunnelState.Up && !await HasLiveEgressAsync(raw.AdapterName, CancellationToken.None))
        {
            raw = new TunnelStatus
            {
                State = TunnelState.Degraded,
                ActiveProviderId = raw.ActiveProviderId,
                ActiveProviderName = raw.ActiveProviderName,
                AdapterName = raw.AdapterName,
                Detail = $"{raw.Detail} · no live egress on tunnel",
                Providers = raw.Providers
            };
        }
        var tunnel = Stabilize(raw);
        LastTunnelStatus = tunnel;
        if (tunnel.State != TunnelState.Up)
            issues.Add(tunnel.State == TunnelState.Degraded
                ? "VPN-клиент открыт, но туннель не подключён"
                : "VPN/туннель не обнаружен");

        var geo = await _location.RefreshAsync();
        _lastGeo = DateTimeOffset.Now;
        if (!geo.Ok)
            issues.Add("Geo-проверка недоступна");
        else if (_settings.StrictLocation && !_location.MatchesTarget(_settings))
            issues.Add($"Локация {geo.CountryCode} ≠ цель {_settings.TargetCountryCode}");

        var pingMs = await ProbeLatencyAsync();
        if (pingMs is null)
            issues.Add("Пинг недоступен");
        else
            _lastLatencyMs = pingMs;

        LastAuditAt = DateTimeOffset.Now;
        LastAuditOk = issues.Count == 0;
        LastAuditDetail = LastAuditOk
            ? "Все проверки пройдены"
            : string.Join("; ", issues);
        _journal.Add("audit", LastAuditDetail);
        _notifier.Balloon(
            LastAuditOk ? "Аудит OK" : "Аудит: есть замечания",
            LastAuditDetail,
            LastAuditOk ? ToolTipIcon.Info : ToolTipIcon.Warning);
        StateChanged?.Invoke();
    }

    private void OnDetectedStart(int pid)
    {
        // Every WMI-seen Claude start counts as an attempt (allowed or not).
        Interlocked.Increment(ref _launchAttempts);
        PersistStats();
        StateChanged?.Invoke();
    }

    private void OnBlockedLaunch(int pid, string reason)
    {
        Interlocked.Increment(ref _blockedStarts);
        PersistStats();
        _journal.Add("launch", $"Попытка запуска Claude — заблокирована (PID {pid}: {reason})");
        StateChanged?.Invoke();
    }

    private void OnSuspended()
    {
        // Enforce-suspend of already-running Claude is not a launch attempt.
        Interlocked.Increment(ref _suspendEvents);
        PersistStats();
    }

    private void PersistStats()
    {
        var spark = _activitySpark.ToArray();
        if (spark.Length < 12)
            spark = spark.Concat(Enumerable.Repeat(0, 12 - spark.Length)).ToArray();
        StatsStore.Save(new PersistentStats
        {
            BlockedStarts = _blockedStarts,
            LaunchAttempts = _launchAttempts,
            SuspendEvents = _suspendEvents,
            ActivitySpark = spark.TakeLast(12).ToArray(),
            SparkHourUnix = _sparkHourUnix > 0
                ? _sparkHourUnix
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600
        });
    }

    private void RestoreActivitySpark(PersistentStats stats)
    {
        var buckets = stats.ActivitySpark is { Length: > 0 }
            ? stats.ActivitySpark.ToList()
            : Enumerable.Repeat(0, 12).ToList();
        while (buckets.Count < 12) buckets.Insert(0, 0);
        if (buckets.Count > 12) buckets = buckets.TakeLast(12).ToList();

        var nowHour = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
        var age = stats.SparkHourUnix > 0 ? Math.Max(0, nowHour - stats.SparkHourUnix) : 12;
        if (age >= 12)
            buckets = Enumerable.Repeat(0, 12).ToList();
        else if (age > 0)
        {
            for (var i = 0; i < age; i++)
            {
                buckets.RemoveAt(0);
                buckets.Add(0);
            }
        }

        _activitySpark.Clear();
        foreach (var v in buckets)
            _activitySpark.Enqueue(v);
        _sparkHourUnix = stats.SparkHourUnix > 0
            ? Math.Min(stats.SparkHourUnix + age, nowHour)
            : nowHour;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (Exception ex) { _journal.Add("error", ex.Message); }

            try { await Task.Delay(Math.Clamp(_settings.PollIntervalMs, 400, 5000), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        AppSettings settings;
        lock (_gate) { settings = _settings; }

        if (_temporaryAllowUntil is { } until && until <= DateTimeOffset.Now)
        {
            _temporaryAllowUntil = null;
            _temporaryAllowStartedAt = null;
            _processes.SetAllowed(false);
            _processes.Enforce(false, settings.KillInsteadOfSuspend);
            ClaudeAllowed = false;
        }

        var snapshot = SystemSnapshot.Capture();
        var raw = _tunnel.Evaluate(snapshot);

        // Live egress via the claimed tunnel adapter — require 2 failed samples before demoting Up
        // (one flaky TCP must not false-kill when KillInsteadOfSuspend is on).
        if (raw.State == TunnelState.Up)
        {
            if (!await HasLiveEgressAsync(raw.AdapterName, ct))
            {
                _egressFailVotes++;
                if (_egressFailVotes >= 2)
                {
                    raw = new TunnelStatus
                    {
                        State = TunnelState.Degraded,
                        ActiveProviderId = raw.ActiveProviderId,
                        ActiveProviderName = raw.ActiveProviderName,
                        AdapterName = raw.AdapterName,
                        Detail = $"{raw.Detail} · no live egress on tunnel",
                        Providers = raw.Providers
                    };
                }
            }
            else
            {
                _egressFailVotes = 0;
            }
        }
        else
        {
            _egressFailVotes = 0;
        }

        var tunnel = Stabilize(raw);
        LastTunnelStatus = tunnel;

        // Close stale WMI allow before geo await when tunnel already left Up (unless temp-allow override).
        if (tunnel.State != TunnelState.Up && !TemporaryAllowActive && settings.GuardEnabled)
        {
            _processes.SetAllowed(false);
            ClaudeAllowed = false;
        }

        // Strict location: re-check geo often even when tunnel stays Up (node switch).
        var geoInterval = settings.StrictLocation && tunnel.State == TunnelState.Up
            ? Math.Min(10, Math.Max(5, settings.GeoCheckIntervalSeconds))
            : Math.Max(15, settings.GeoCheckIntervalSeconds);
        var needGeo = DateTimeOffset.Now - _lastGeo >= TimeSpan.FromSeconds(geoInterval);
        var tunnelEdge = _stableTunnel != tunnel.State;
        if (needGeo || _location.LastResult is null || tunnelEdge)
        {
            var geo = await _location.RefreshAsync(ct);
            _lastGeo = DateTimeOffset.Now;
            var pingMs = await ProbeLatencyAsync();
            if (pingMs is long p)
                _lastLatencyMs = p;
            else if (needGeo)
                _lastLatencyMs = null;

            if (geo.Ok && _location.CountryChanged(_lastCountry))
            {
                _journal.Add("geo", $"Смена IP/страны: {_lastCountry ?? "?"} → {geo.CountryCode} ({geo.Ip}) · {geo.Source}");
                if (!string.IsNullOrEmpty(_lastCountry))
                {
                    _notifier.Balloon(
                        "Смена локации",
                        $"Сейчас {geo.CountryName} ({geo.CountryCode}). Держите одну стабильную страну.",
                        ToolTipIcon.Warning);
                }
                _lastCountry = geo.CountryCode;
                RecordExit(geo, "country-change");
            }
            else if (geo.Ok)
            {
                _lastCountry = geo.CountryCode;
                RecordExit(geo, needGeo ? "poll" : "edge");
            }
            else
            {
                RecordExit(geo, "geo-fail");
            }
        }

        var geoReliable = _location.LastResult is { Ok: true };
        var locationMatches = geoReliable && _location.MatchesTarget(settings);
        // Strict: unknown geo blocks allow; non-strict: ignore location
        var locationOk = !settings.StrictLocation || locationMatches;
        var vpnLive = tunnel.State == TunnelState.Up;
        var allow = settings.GuardEnabled && vpnLive && locationOk;

        if (settings.StrictLocation && !geoReliable)
            allow = false;

        if (!settings.GuardEnabled || TemporaryAllowActive)
            allow = true;

        ClaudeAllowed = allow;
        // Publish deny immediately so WMI cannot race on a stale allow across the poll gap.
        _processes.SetKillMode(settings.KillInsteadOfSuspend);
        if (!allow)
            _processes.SetAllowed(false);
        _processes.Enforce(allow, settings.KillInsteadOfSuspend);
        if (allow)
            _processes.SetAllowed(true);
        ClaudeProcesses = _processes.ListClaudeProcesses();

        var passportBefore = _passport.Current;
        var passportNow = _passport.Observe(_location.LastResult, tunnel.ActiveProviderId, vpnLive);

        if (vpnLive && ClaudeProcesses.Count > 0)
            _hadClaudeWhileUp = true;

        if (_stableTunnel == TunnelState.Up && tunnel.State != TunnelState.Up)
        {
            // Temp-allow intentionally overrides killswitch — keep WMI/UI aligned with `allow`.
            if (!TemporaryAllowActive)
                _processes.SetAllowed(false); // close WMI race before journal/notify work
            _journal.Add("vpn", tunnel.State == TunnelState.Degraded
                ? "VPN-клиент есть, туннель неактивен — Claude приостановлен"
                : "VPN отключён — процессы Claude приостановлены");
            _notifier.Balloon(
                "VPN отключён",
                $"Включите VPN и локацию {settings.TargetLocationLabel} ({settings.TargetCountryCode}).",
                ToolTipIcon.Error);
            ReminderText = UiCopy.ReminderVpn(settings);
            if (_hadClaudeWhileUp || ClaudeProcesses.Count > 0)
            {
                RaiseIncident("vpn-drop", snapshot, tunnel, passportBefore, passportNow,
                    tunnel.State == TunnelState.Degraded
                        ? "Туннель потерян (Degraded) во время сессии Claude"
                        : "VPN Down во время сессии Claude");
                _hadClaudeWhileUp = false;
            }
        }
        else if (_stableTunnel != TunnelState.Up && tunnel.State == TunnelState.Up)
        {
            _journal.Add("vpn", $"VPN подключён через {tunnel.ActiveProviderName}");
            if (settings.RelaunchClaudeOnVpnRestore && allow)
            {
                var n = _processes.RelaunchRemembered();
                if (n > 0)
                    _notifier.Balloon("Claude запущен", $"Автозапуск после восстановления VPN ({n})", ToolTipIcon.Info);
            }
        }

        // Strict geo-break only when geo is still reliable but country left target — not on geo outage.
        if (settings.GuardEnabled && settings.StrictLocation && vpnLive && geoReliable &&
            _locationWasOk && !locationMatches && (_hadClaudeWhileUp || ClaudeProcesses.Count > 0))
        {
            RaiseIncident("geo-break", snapshot, tunnel, passportBefore, passportNow,
                $"Exit ушёл с цели {settings.TargetCountryCode} при живом туннеле");
        }
        // Preserve prior ok across geo outage so recovery onto a wrong country still edges geo-break.
        if (!settings.StrictLocation)
            _locationWasOk = true;
        else if (geoReliable)
            _locationWasOk = locationMatches;

        // Keep recovery banner in sync with current failure mode (do not leave stale "turn on VPN"
        // after the tunnel returns while geo still blocks).
        UiCopy.SetLang(settings);
        if (!settings.GuardEnabled || TemporaryAllowActive || allow)
            ReminderText = "";
        else if (!vpnLive)
            ReminderText = UiCopy.ReminderVpn(settings);
        else if (settings.StrictLocation && !geoReliable)
            ReminderText = UiCopy.ReminderGeoUnreliable(settings); // tunnel Up, geo flaky — not "turn on VPN"
        else if (settings.StrictLocation && !locationMatches)
            ReminderText = UiCopy.ReminderWrongExit(settings, _location.LastCountryCode);
        else
            ReminderText = "";

        if (_location.ShouldRemind(settings, vpnLive) && settings.GuardEnabled && !TemporaryAllowActive)
        {
            var msg = !vpnLive
                ? UiCopy.ReminderVpnAndLoc(settings)
                : !geoReliable
                    ? UiCopy.ReminderGeoUnreliable(settings)
                    : settings.StrictLocation
                        ? UiCopy.ReminderWrongExit(settings, _location.LastCountryCode)
                        : null;
            if (msg is not null)
            {
                _notifier.Balloon(
                    UiCopy.Lang(settings) == "en" ? "Location reminder" :
                    UiCopy.Lang(settings) == "zh" ? "地区提醒" : "Напоминание о локации",
                    msg, ToolTipIcon.Info);
                _location.MarkReminded();
                _journal.Add("remind", msg);
            }
        }

        AccountRating = ComputeRating(settings, tunnel);
        StatusSummary = BuildSummary(settings, tunnel, allow);
        PushSparks(tunnel, allow);
        DetectWmiStatus();
        _lastTunnel = tunnel.State;
        _stableTunnel = tunnel.State;
        StateChanged?.Invoke();
    }

    private TunnelStatus Stabilize(TunnelStatus raw)
    {
        var need = Math.Clamp(_settings.TunnelConfirmSamples, 1, 5);
        if (raw.State == TunnelState.Up)
        {
            _upVotes++;
            _downVotes = 0;
            if (_upVotes >= need || _stableTunnel == TunnelState.Up)
                return raw;
            return CloneWithState(raw, _stableTunnel == TunnelState.Degraded ? TunnelState.Degraded : TunnelState.Down,
                "confirming tunnel…");
        }

        if (raw.State == TunnelState.Degraded)
        {
            _upVotes = 0;
            _downVotes++;
            // Degraded is never reported as Up.
            return raw;
        }

        // Fast Down: never keep reporting Up after a real disconnect sample.
        _upVotes = 0;
        _downVotes++;
        return raw;
    }

    private static TunnelStatus CloneWithState(TunnelStatus src, TunnelState state, string detail) =>
        new()
        {
            State = state,
            ActiveProviderId = src.ActiveProviderId,
            ActiveProviderName = src.ActiveProviderName,
            AdapterName = src.AdapterName,
            Detail = detail,
            Providers = src.Providers
        };

    private static async Task<bool> HasLiveEgressAsync(string? adapterName, CancellationToken ct)
    {
        // Prefer proving the claimed tunnel adapter still has a usable unicast address.
        if (!string.IsNullOrWhiteSpace(adapterName))
        {
            try
            {
                // Exact name or description Contains(adapterName) only — never adapterName.Contains(nic.Name)
                // (that reverse match binds wrong NICs like short "VPN").
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n =>
                        n.OperationalStatus == OperationalStatus.Up &&
                        (n.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase) ||
                         n.Description.Equals(adapterName, StringComparison.OrdinalIgnoreCase) ||
                         n.Description.Contains(adapterName, StringComparison.OrdinalIgnoreCase)));
                if (nic is null)
                    return false;

                var ipProps = nic.GetIPProperties();
                var local = ipProps.UnicastAddresses
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                         !System.Net.IPAddress.IsLoopback(a));
                if (local is null)
                    return false;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(1200);
                using var tcp = new TcpClient();
                tcp.Client.Bind(new System.Net.IPEndPoint(local, 0));
                await tcp.ConnectAsync("1.1.1.1", 443, cts.Token);
                return tcp.Connected;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1200);
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("1.1.1.1", 443, cts.Token);
            return tcp.Connected;
        }
        catch
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("1.1.1.1", 900);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void RecordExit(GeoResult geo, string note) =>
        ExitHistoryStore.Append(new ExitHistoryEntry
        {
            Ok = geo.Ok,
            Ip = geo.Ip,
            CountryCode = geo.CountryCode,
            CountryName = geo.CountryName,
            Source = geo.Source,
            Note = note,
            Timestamp = DateTimeOffset.Now
        });

    private void RaiseIncident(
        string kind,
        SystemSnapshot snapshot,
        TunnelStatus tunnel,
        PassportSnapshot before,
        PassportSnapshot after,
        string summary)
    {
        var geo = _location.LastResult;
        var (incident, isNew) = IncidentStore.AppendResult(new GuardIncident
        {
            Kind = kind,
            Summary = summary,
            PassportBefore = before.Fingerprint,
            PassportAfter = after.Fingerprint,
            ProviderId = tunnel.ActiveProviderId,
            ProviderName = tunnel.ActiveProviderName,
            AdapterName = tunnel.AdapterName,
            ExitIp = geo?.Ip,
            ExitCountry = geo?.CountryCode,
            GeoSource = geo?.Source,
            GeoAgreement = geo?.AgreementCount ?? 0,
            GeoSamples = geo?.SampleCount ?? 0,
            AdaptersUp = snapshot.Adapters
                .Where(a => a.Status == OperationalStatus.Up)
                .Select(a => $"{a.Name} ({a.Description})")
                .Take(12)
                .ToList(),
            ClaudeProcesses = ClaudeProcesses
                .Select(p => $"{p.Kind} pid={p.Id}")
                .Take(12)
                .ToList(),
            Detail = tunnel.Detail
        });
        if (!isNew) return;
        _journal.Add("incident", $"Инцидент {incident.Id}: {summary}");
        _notifier.Balloon("Incident Freeze Pack", summary, ToolTipIcon.Error);
    }

    private async Task<long?> ProbeLatencyAsync()
    {
        // Prefer TCP connect timing via tunnel adapter when known; fallback ICMP default route.
        var adapter = LastTunnelStatus?.AdapterName;
        if (!string.IsNullOrWhiteSpace(adapter))
        {
            var sw = Stopwatch.StartNew();
            var ok = await HasLiveEgressAsync(adapter, CancellationToken.None);
            sw.Stop();
            if (ok)
            {
                LastLatencyMode = "adapter-egress";
                return sw.ElapsedMilliseconds;
            }
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1500);
            if (reply.Status == IPStatus.Success)
            {
                LastLatencyMode = "icmp-default";
                return reply.RoundtripTime;
            }
        }
        catch
        {
            /* ignore */
        }
        LastLatencyMode = "icmp-default";
        return null;
    }

    private void PushSparks(TunnelStatus tunnel, bool allow)
    {
        var deltaBlocks = Math.Max(0, _blockedStarts - _prevBlocked);
        var deltaAttempts = Math.Max(0, _launchAttempts - _prevAttempts);
        _prevBlocked = _blockedStarts;
        _prevAttempts = _launchAttempts;
        var activity = deltaBlocks + deltaAttempts;
        if (_journal.Entries.Count > 0)
        {
            var recent = _journal.Entries.Count(e =>
                e.Timestamp > DateTimeOffset.Now.AddMinutes(-2) &&
                e.Kind is "launch" or "suspend" or "kill" or "vpn" or "error");
            activity = Math.Max(activity, recent);
        }

        // Hourly buckets: bump current hour, roll when UTC hour changes.
        EnsureSparkHourAligned();
        if (_activitySpark.Count == 0)
            for (var i = 0; i < 12; i++) _activitySpark.Enqueue(0);
        var arr = _activitySpark.ToArray();
        arr[^1] = Math.Max(arr[^1], activity);
        _activitySpark.Clear();
        foreach (var v in arr) _activitySpark.Enqueue(v);
        // Disk persist happens on block/suspend/dispose — not every poll tick.

        var health = tunnel.State == TunnelState.Up ? 28.0 : tunnel.State == TunnelState.Degraded ? 14.0 : 8.0;
        if (allow) health += 10;
        if (_lastLatencyMs is long ms)
            health += Math.Clamp(40 - ms / 20.0, -8, 12);
        Enqueue(_protectionSpark, Math.Clamp(health, 4, 48), 24);
    }

    private long _sparkHourUnix;

    private void EnsureSparkHourAligned()
    {
        var nowHour = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
        if (_sparkHourUnix == 0)
            _sparkHourUnix = nowHour;
        while (_sparkHourUnix < nowHour)
        {
            Enqueue(_activitySpark, 0, 12);
            _sparkHourUnix++;
        }
    }

    private static void Enqueue(Queue<int> q, int v, int max)
    {
        q.Enqueue(v);
        while (q.Count > max) q.Dequeue();
    }

    private static void Enqueue(Queue<double> q, double v, int max)
    {
        q.Enqueue(v);
        while (q.Count > max) q.Dequeue();
    }

    private void DetectWmiStatus()
    {
        if (_journal.Entries.Any(e => e.Message.Contains("WMI process start watch armed", StringComparison.OrdinalIgnoreCase)))
            _wmiAvailableSticky = true;
        if (_journal.Entries.Any(e => e.Message.Contains("WMI unavailable", StringComparison.OrdinalIgnoreCase)))
            _wmiAvailableSticky ??= false;
        WmiAvailable = _wmiAvailableSticky != false;
    }

    public IReadOnlyList<(string Title, string Body, string Tone)> BuildRecommendations()
    {
        var list = new List<(string, string, string)>();
        var vpnUp = LastTunnelStatus?.State == TunnelState.Up;
        var degraded = LastTunnelStatus?.State == TunnelState.Degraded;
        var geoOk = LastGeo?.Ok == true;
        var locOk = !Settings.StrictLocation || _location.MatchesTarget(Settings);

        if (degraded)
            list.Add(("VPN не подключён", "Клиент запущен, но туннель down — Claude блокируется.", "warn"));
        else if (!vpnUp)
            list.Add(("VPN не обнаружен", "Туннель сейчас down — Claude будет блокироваться.", "warn"));
        else if (!locOk)
            list.Add(("Локация не совпадает",
                $"Сейчас {LastGeo?.CountryCode ?? "?"}, цель {Settings.TargetCountryCode}.", "warn"));

        if (!WmiAvailable)
            list.Add(("WMI недоступен", "Используется polling процессов. Полный watch не работает.", "warn"));

        if (LastAuditAt is null)
            list.Add(("Аудит не запускался", "Нажмите «Запустить аудит» — без него статус проверок неизвестен.", "warn"));
        else if (!LastAuditOk)
            list.Add(("Аудит с замечаниями", LastAuditDetail, "warn"));

        var dead = LastTunnelStatus?.Providers.Count(p => p.Enabled && !p.Healthy) ?? 0;
        if (dead > 0)
            list.Add(("Провайдеры без туннеля",
                $"{dead} включённых VPN-клиентов не дают живой адаптер.", "info"));

        list.Add(("Лимиты Claude", "Burn дневной/недельной квоты мы не видим — только вы.", "muted"));
        list.Add(("Linux / веб-сессии", "Guard не отслеживает браузер и Linux-клиенты.", "muted"));
        list.Add(("Другие устройства", "Входы с телефонов/других ПК не проверяются.", "muted"));
        list.Add(("Usage Policy", "Jailbreak и вредоносные промпты Guard не фильтрует.", "muted"));

        if (geoOk && vpnUp && locOk && WmiAvailable && LastAuditOk)
            list.RemoveAll(x => x.Item3 is "warn" or "info");

        return list.Take(6).Select(x => (x.Item1, x.Item2, x.Item3)).ToList();
    }

    private int ComputeRating(AppSettings settings, TunnelStatus tunnel)
    {
        // Posture score: current tunnel/geo/guard health. Recent blocks/suspends lower trust.
        var score = 55;
        if (settings.GuardEnabled) score += 10;
        if (tunnel.State == TunnelState.Up) score += 15;
        else if (tunnel.State == TunnelState.Degraded) score += 3;
        if (_location.MatchesTarget(settings)) score += 12;
        if (settings.StrictLocation) score += 5;

        var since = DateTimeOffset.Now - TimeSpan.FromHours(24);
        var launches24h = _journal.Entries.Count(e =>
            e.Kind.Equals("launch", StringComparison.OrdinalIgnoreCase) && e.Timestamp >= since);
        var suspends24h = _journal.Entries.Count(e =>
            e.Kind.Equals("suspend", StringComparison.OrdinalIgnoreCase) && e.Timestamp >= since);
        score -= Math.Min(12, launches24h * 2);
        score -= Math.Min(8, suspends24h);

        if (TemporaryAllowActive) score -= 8;
        if (_lastTunnel != TunnelState.Up) score -= 5;
        if (!WmiAvailable) score -= 4;
        if (LastAuditAt is null) score -= 3;
        else if (!LastAuditOk) score -= 6;
        return Math.Clamp(score, 20, 98);
    }

    private string BuildSummary(AppSettings settings, TunnelStatus tunnel, bool allow)
    {
        UiCopy.SetLang(settings);
        if (TemporaryAllowActive) return UiCopy.SummaryTempAllow();
        if (!settings.GuardEnabled) return UiCopy.SummaryGuardOff();
        if (tunnel.State == TunnelState.Degraded) return UiCopy.SummaryDegraded();
        if (tunnel.State != TunnelState.Up) return UiCopy.SummaryVpnDown();
        if (settings.StrictLocation && !_location.MatchesTarget(settings))
            return UiCopy.SummaryWrongLoc(_location.LastCountryCode, settings.TargetCountryCode);
        if (allow) return UiCopy.SummaryOk(tunnel.ActiveProviderName, _location.LastCountryCode);
        return UiCopy.SummaryWait();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1000); } catch { /* ignore */ }
        _processes.Blocked -= OnBlockedLaunch;
        _processes.DetectedStart -= OnDetectedStart;
        _processes.Suspended -= OnSuspended;
        PersistStats();
        _processes.Dispose();
        _location.Dispose();
        _cts?.Dispose();
    }
}
