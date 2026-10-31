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
    private bool _lastAllow;
    private DateTimeOffset _lastGeo = DateTimeOffset.MinValue;

    public event Action? StateChanged;

    public GuardService(EventJournal journal, Notifier notifier)
    {
        _journal = journal;
        _notifier = notifier;
        _settings = SettingsStore.Load();
        _tunnel = new TunnelMonitor(CreateProviders(), () => _settings);
        _processes = new ProcessGuard(journal);
    }

    public AppSettings Settings => _settings;
    public EventJournal Journal => _journal;
    public TunnelStatus? LastTunnelStatus { get; private set; }
    public GeoResult? LastGeo => _location.LastResult;
    public IReadOnlyList<ClaudeProcessInfo> ClaudeProcesses { get; private set; } = [];
    public bool ClaudeAllowed { get; private set; }
    public string StatusSummary { get; private set; } = "Starting…";
    public string ReminderText { get; private set; } = "";

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
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _journal.Add("boot", "Claude VPN Guard started");
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
            SettingsStore.Save(settings);
            AutostartManager.SetEnabled(settings.StartWithWindows);
        }
        _journal.Add("settings", $"Saved. Target={settings.TargetCountryCode}, strict={settings.StrictLocation}");
        StateChanged?.Invoke();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
            }
            catch (Exception ex)
            {
                _journal.Add("error", ex.Message);
            }

            try
            {
                await Task.Delay(Math.Clamp(_settings.PollIntervalMs, 400, 5000), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        AppSettings settings;
        lock (_gate) { settings = _settings; }

        var snapshot = SystemSnapshot.Capture();
        var tunnel = _tunnel.Evaluate(snapshot);
        LastTunnelStatus = tunnel;

        var needGeo = DateTimeOffset.Now - _lastGeo >=
                      TimeSpan.FromSeconds(Math.Max(15, settings.GeoCheckIntervalSeconds));
        if (needGeo || _location.LastResult is null)
        {
            var geo = await _location.RefreshAsync(ct);
            _lastGeo = DateTimeOffset.Now;
            if (geo.Ok && _location.CountryChanged(_lastCountry))
            {
                _journal.Add("geo", $"Exit country {_lastCountry ?? "?"} → {geo.CountryCode} ({geo.Ip})");
                if (!string.IsNullOrEmpty(_lastCountry))
                {
                    _notifier.Balloon(
                        "Exit country changed",
                        $"Now {geo.CountryName} ({geo.CountryCode}). Keep one stable location.",
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

        // Warm-up: if geo unknown while strict, don't allow
        if (settings.StrictLocation && (_location.LastResult is null || !_location.LastResult.Ok))
            allow = false;

        if (!settings.GuardEnabled)
            allow = true;

        ClaudeAllowed = allow;
        _processes.SetAllowed(allow);
        _processes.Enforce(allow, settings.KillInsteadOfSuspend);
        ClaudeProcesses = _processes.ListClaudeProcesses();

        if (_lastTunnel == TunnelState.Up && tunnel.State == TunnelState.Down)
        {
            _journal.Add("vpn", "Tunnel dropped — Claude suspended");
            _notifier.Balloon(
                "VPN dropped",
                $"Turn on VPN and select {settings.TargetLocationLabel} ({settings.TargetCountryCode}).",
                ToolTipIcon.Error);
            ReminderText = $"Enable VPN → {settings.TargetLocationLabel}";
        }
        else if (_lastTunnel == TunnelState.Down && tunnel.State == TunnelState.Up)
        {
            _journal.Add("vpn", $"Tunnel up via {tunnel.ActiveProviderName}");
        }

        if (_location.ShouldRemind(settings, tunnel.State == TunnelState.Up) && settings.GuardEnabled)
        {
            var msg = tunnel.State != TunnelState.Up
                ? $"Turn on your VPN and choose {settings.TargetLocationLabel} ({settings.TargetCountryCode})."
                : $"VPN is up, but exit is {_location.LastCountryCode ?? "?"}. Switch node to {settings.TargetLocationLabel}.";

            ReminderText = msg;
            _notifier.Balloon("Location reminder", msg, ToolTipIcon.Info);
            _location.MarkReminded();
            _journal.Add("remind", msg);
        }
        else if (allow)
        {
            ReminderText = "";
        }

        StatusSummary = BuildSummary(settings, tunnel, allow);
        _lastTunnel = tunnel.State;
        _lastAllow = allow;
        StateChanged?.Invoke();
    }

    private string BuildSummary(AppSettings settings, TunnelStatus tunnel, bool allow)
    {
        if (!settings.GuardEnabled) return "Guard paused — Claude unrestricted";
        if (tunnel.State != TunnelState.Up) return "VPN down — Claude blocked";
        if (settings.StrictLocation && !_location.MatchesTarget(settings))
            return $"Wrong location ({_location.LastCountryCode ?? "?"}) — need {settings.TargetCountryCode}";
        if (allow) return $"Protected — {tunnel.ActiveProviderName} / {_location.LastCountryCode ?? "…"}";
        return "Waiting for tunnel + location";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1000); } catch { /* ignore */ }
        _processes.Dispose();
        _location.Dispose();
        _cts?.Dispose();
    }
}
