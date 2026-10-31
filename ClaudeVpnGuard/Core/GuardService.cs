using System.Diagnostics;
using System.Net.NetworkInformation;
using ClaudeVpnGuard.Tunnel;
using ClaudeVpnGuard.Tunnel.Providers;

namespace ClaudeVpnGuard.Core;

public sealed class GuardService : IDisposable
{
    private readonly TunnelMonitor _tunnel;
    private readonly ProcessGuard _processes;
    private readonly LocationWatch _location = new();
    private readonly EventJournal _journal;
    private readonly Notifier _notifier;
    private readonly object _gate = new();

    private AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private TunnelState _lastTunnel = TunnelState.Down;
    private string? _lastCountry;
    private DateTimeOffset _lastGeo = DateTimeOffset.MinValue;
    private DateTimeOffset? _temporaryAllowUntil;
    private int _blockedStarts;
    private int _launchAttempts;
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
        _tunnel = new TunnelMonitor(CreateProviders(), () => _settings);
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
    public long? LastLatencyMs => _lastLatencyMs;
    public AppSettings Settings => _settings;
    public EventJournal Journal => _journal;
    public TunnelStatus? LastTunnelStatus { get; private set; }
    public GeoResult? LastGeo => _location.LastResult;
    public IReadOnlyList<ClaudeProcessInfo> ClaudeProcesses { get; private set; } = [];
    public bool ClaudeAllowed { get; private set; }
    public bool TemporaryAllowActive =>
        _temporaryAllowUntil is { } until && until > DateTimeOffset.Now;
    public string StatusSummary { get; private set; } = "Запуск…";
    public string ReminderText { get; private set; } = "";
    public int AccountRating { get; private set; } = 70;
    public int[] ActivitySpark => _activitySpark.ToArray();
    public double[] ProtectionSpark => _protectionSpark.ToArray();
    public bool WmiAvailable { get; private set; } = true;

    public static IEnumerable<ITunnelProvider> CreateProviders() =>
    [
        new HappProvider(),
        new HiddifyProvider(),
        new V2rayNProvider(),
        new ClashVergeProvider(),
        new AmneziaProvider(),
        new WireGuardProvider(),
        new WindscribeProvider(),
        new OutlineProvider(),
        new NekorayProvider()
    ];

    public void Start()
    {
        AutostartManager.SetEnabled(_settings.StartWithWindows);
        _processes.StartWatching();
        _processes.Blocked += OnBlockedLaunch;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _journal.Add("boot", "Claude Code Guardian запущен");
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

    public void AllowLaunchTemporarily(TimeSpan duration)
    {
        _temporaryAllowUntil = DateTimeOffset.Now.Add(duration);
        _journal.Add("override", $"Временный запуск Claude разрешён на {(int)duration.TotalMinutes} мин");
        _notifier.Balloon("Временный доступ", $"Claude разрешён на {(int)duration.TotalMinutes} минут", ToolTipIcon.Info);
        StateChanged?.Invoke();
    }

    public void RevokeTemporaryAllow()
    {
        _temporaryAllowUntil = null;
        _journal.Add("override", "Временный доступ отозван");
    }

    public async Task CheckVpnAsync()
    {
        var sw = Stopwatch.StartNew();
        var geo = await _location.RefreshAsync();
        sw.Stop();
        _lastLatencyMs = sw.ElapsedMilliseconds;
        _lastGeo = DateTimeOffset.Now;
        if (geo.Ok)
        {
            _journal.Add("vpn", $"Проверка VPN: {geo.CountryName} ({geo.CountryCode}) · {geo.Ip} · {sw.ElapsedMilliseconds} мс");
            _notifier.Balloon("VPN проверен", $"{geo.CountryName} · {geo.Ip} · {sw.ElapsedMilliseconds} мс", ToolTipIcon.Info);
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
        var tunnel = _tunnel.Evaluate(snapshot);
        LastTunnelStatus = tunnel;
        if (tunnel.State != TunnelState.Up)
            issues.Add("VPN/туннель не обнаружен");

        var geo = await _location.RefreshAsync();
        _lastGeo = DateTimeOffset.Now;
        if (!geo.Ok)
            issues.Add("Geo-проверка недоступна");
        else if (_settings.StrictLocation && !_location.MatchesTarget(_settings))
            issues.Add($"Локация {geo.CountryCode} ≠ цель {_settings.TargetCountryCode}");

        try
        {
            var ping = new Ping();
            var reply = await ping.SendPingAsync("1.1.1.1", 1500);
            if (reply.Status != IPStatus.Success)
                issues.Add("Пинг 1.1.1.1 неуспешен");
            else
                _lastLatencyMs = reply.RoundtripTime;
        }
        catch
        {
            issues.Add("Пинг недоступен");
        }

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

    private void OnBlockedLaunch(int pid, string reason)
    {
        Interlocked.Increment(ref _launchAttempts);
        Interlocked.Increment(ref _blockedStarts);
        _journal.Add("launch", $"Попытка запуска Claude — заблокирована (PID {pid}: {reason})");
        StateChanged?.Invoke();
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
            _temporaryAllowUntil = null;

        var snapshot = SystemSnapshot.Capture();
        var tunnel = _tunnel.Evaluate(snapshot);
        LastTunnelStatus = tunnel;

        var needGeo = DateTimeOffset.Now - _lastGeo >=
                      TimeSpan.FromSeconds(Math.Max(15, settings.GeoCheckIntervalSeconds));
        if (needGeo || _location.LastResult is null)
        {
            var sw = Stopwatch.StartNew();
            var geo = await _location.RefreshAsync(ct);
            sw.Stop();
            _lastLatencyMs ??= sw.ElapsedMilliseconds;
            _lastGeo = DateTimeOffset.Now;
            if (geo.Ok && _location.CountryChanged(_lastCountry))
            {
                _journal.Add("geo", $"Смена IP/страны: {_lastCountry ?? "?"} → {geo.CountryCode} ({geo.Ip})");
                if (!string.IsNullOrEmpty(_lastCountry))
                {
                    _notifier.Balloon(
                        "Смена локации",
                        $"Сейчас {geo.CountryName} ({geo.CountryCode}). Держите одну стабильную страну.",
                        ToolTipIcon.Warning);
                }
                _lastCountry = geo.CountryCode;
            }
            else if (geo.Ok)
            {
                _lastCountry = geo.CountryCode;
            }
        }

        var locationOk = !settings.StrictLocation || _location.MatchesTarget(settings);
        var allow = settings.GuardEnabled &&
                    tunnel.State == TunnelState.Up &&
                    locationOk;

        if (settings.StrictLocation && (_location.LastResult is null || !_location.LastResult.Ok))
            allow = false;

        if (!settings.GuardEnabled || TemporaryAllowActive)
            allow = true;

        ClaudeAllowed = allow;
        _processes.SetAllowed(allow);
        _processes.Enforce(allow, settings.KillInsteadOfSuspend);
        ClaudeProcesses = _processes.ListClaudeProcesses();

        if (_lastTunnel == TunnelState.Up && tunnel.State == TunnelState.Down)
        {
            _journal.Add("vpn", "VPN отключён — процессы Claude приостановлены");
            _notifier.Balloon(
                "VPN отключён",
                $"Включите VPN и локацию {settings.TargetLocationLabel} ({settings.TargetCountryCode}).",
                ToolTipIcon.Error);
            ReminderText = $"Включите VPN → {settings.TargetLocationLabel}";
        }
        else if (_lastTunnel == TunnelState.Down && tunnel.State == TunnelState.Up)
        {
            _journal.Add("vpn", $"VPN подключён через {tunnel.ActiveProviderName}");
        }

        if (_location.ShouldRemind(settings, tunnel.State == TunnelState.Up) && settings.GuardEnabled && !TemporaryAllowActive)
        {
            var msg = tunnel.State != TunnelState.Up
                ? $"Включите VPN и выберите локацию {settings.TargetLocationLabel} ({settings.TargetCountryCode})."
                : $"VPN активен, но выход {_location.LastCountryCode ?? "?"}. Переключите ноду на {settings.TargetLocationLabel}.";

            ReminderText = msg;
            _notifier.Balloon("Напоминание о локации", msg, ToolTipIcon.Info);
            _location.MarkReminded();
            _journal.Add("remind", msg);
        }
        else if (allow)
        {
            ReminderText = "";
        }

        AccountRating = ComputeRating(settings, tunnel);
        StatusSummary = BuildSummary(settings, tunnel, allow);
        PushSparks(tunnel, allow);
        DetectWmiStatus();
        _lastTunnel = tunnel.State;
        StateChanged?.Invoke();
    }

    private void PushSparks(TunnelStatus tunnel, bool allow)
    {
        var deltaBlocks = Math.Max(0, _blockedStarts - _prevBlocked);
        var deltaAttempts = Math.Max(0, _launchAttempts - _prevAttempts);
        _prevBlocked = _blockedStarts;
        _prevAttempts = _launchAttempts;
        var activity = deltaBlocks * 3 + deltaAttempts * 2;
        if (_journal.Entries.Count > 0)
        {
            var recent = _journal.Entries.Count(e => e.Timestamp > DateTimeOffset.Now.AddMinutes(-2));
            activity = Math.Max(activity, recent);
        }
        Enqueue(_activitySpark, activity, 12);

        var health = tunnel.State == TunnelState.Up ? 28.0 : 8.0;
        if (allow) health += 10;
        if (_lastLatencyMs is long ms)
            health += Math.Clamp(40 - ms / 20.0, -8, 12);
        Enqueue(_protectionSpark, Math.Clamp(health, 4, 48), 24);
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
        WmiAvailable = !_journal.Entries.Take(20)
            .Any(e => e.Message.Contains("WMI unavailable", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<(string Title, string Body, string Tone)> BuildRecommendations()
    {
        var list = new List<(string, string, string)>();
        var vpnUp = LastTunnelStatus?.State == TunnelState.Up;
        var geoOk = LastGeo?.Ok == true;
        var locOk = !Settings.StrictLocation || _location.MatchesTarget(Settings);

        if (!vpnUp)
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

        // Things Guard cannot verify
        list.Add(("Лимиты Claude", "Burn дневной/недельной квоты мы не видим — только вы.", "muted"));
        list.Add(("Linux / веб-сессии", "Guard не отслеживает браузер и Linux-клиенты.", "muted"));
        list.Add(("Другие устройства", "Входы с телефонов/других ПК не проверяются.", "muted"));
        list.Add(("Usage Policy", "Jailbreak и вредоносные промпты Guard не фильтрует.", "muted"));

        if (geoOk && vpnUp && locOk && WmiAvailable && LastAuditOk)
        {
            // keep unverifiable tips only when all local checks are green
            list.RemoveAll(x => x.Item3 is "warn" or "info");
        }

        return list.Take(6).Select(x => (x.Item1, x.Item2, x.Item3)).ToList();
    }

    private int ComputeRating(AppSettings settings, TunnelStatus tunnel)
    {
        var score = 55;
        if (settings.GuardEnabled) score += 10;
        if (tunnel.State == TunnelState.Up) score += 15;
        if (_location.MatchesTarget(settings)) score += 12;
        if (settings.StrictLocation) score += 5;
        if (_blockedStarts > 0) score += 3;
        if (TemporaryAllowActive) score -= 8;
        if (_lastTunnel == TunnelState.Down) score -= 5;
        if (!WmiAvailable) score -= 4;
        if (LastAuditAt is null) score -= 3;
        else if (!LastAuditOk) score -= 6;
        return Math.Clamp(score, 35, 98);
    }

    private string BuildSummary(AppSettings settings, TunnelStatus tunnel, bool allow)
    {
        if (TemporaryAllowActive) return "Временный доступ к Claude";
        if (!settings.GuardEnabled) return "Защита выключена";
        if (tunnel.State != TunnelState.Up) return "VPN выключен — Claude заблокирован";
        if (settings.StrictLocation && !_location.MatchesTarget(settings))
            return $"Неверная локация ({_location.LastCountryCode ?? "?"}) — нужна {settings.TargetCountryCode}";
        if (allow) return $"Защита активна — {tunnel.ActiveProviderName} / {_location.LastCountryCode ?? "…"}";
        return "Ожидание туннеля и локации";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1000); } catch { /* ignore */ }
        _processes.Blocked -= OnBlockedLaunch;
        _processes.Dispose();
        _location.Dispose();
        _cts?.Dispose();
    }
}
