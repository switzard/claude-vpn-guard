using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeVpnGuard.Core;
using ClaudeVpnGuard.Hygiene;
using ClaudeVpnGuard.Tunnel;

namespace ClaudeVpnGuard.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly GuardService _guard;
    private string _selectedNav = "home";
    private string _statusSummary = "";
    private string _reminder = "";
    private string _protectionTitle = "Защита включена";
    private string _vpnTitle = "VPN отключён";
    private string _claudeTitle = "Claude Code: Заблокирован";
    private string _claudeSubtitle = "";
    private string _vpnIp = "—";
    private string _vpnCountry = "—";
    private string _vpnLatency = "—";
    private string _uptimeText = "00:00:00";
    private string _globalStatus = "Активен";
    private string _systemStatusTitle = "Система активна";
    private string _systemStatusSub = "Все защиты работают";
    private string _auditTime = "—";
    private string _auditDetail = "";
    private bool _auditOk;
    private bool _vpnUp;
    private bool _claudeAllowed;
    private bool _guardEnabled = true;
    private bool _strictLocation = true;
    private bool _killMode;
    private bool _startWithWindows = true;
    private string _targetCode = "NL";
    private string _targetLabel = "Netherlands";
    private int _accountRating = 70;
    private string _eventFilter = "Все";
    private string _autostartFindings = "";

    public MainViewModel(GuardService guard)
    {
        _guard = guard;
        SaveCommand = new RelayCommand(Save);
        DisableClaudeAutostartCommand = new RelayCommand(DisableClaudeAutostart);
        NavigateCommand = new RelayCommand<string>(nav => SelectedNav = nav ?? "home");
        CheckVpnCommand = new AsyncRelayCommand(() => _guard.CheckVpnAsync());
        AllowLaunchCommand = new RelayCommand(() => _guard.AllowLaunchTemporarily(TimeSpan.FromMinutes(15)));
        RunAuditCommand = new AsyncRelayCommand(() => _guard.RunAuditAsync());
        ClearEventsCommand = new RelayCommand(() =>
        {
            _guard.Journal.ClearMemory();
            RefreshFilteredEvents();
        });

        RefreshProviders();
        foreach (var tip in AccountHygiene.Tips)
            HygieneTips.Add(tip);
        foreach (var item in AccountHygiene.Checklist)
            Recommendations.Add(item);

        Countries = KnownCountries.All.Select(c => $"{c.Code} — {c.Label}").ToList();
        SelectedCountry = Countries.FirstOrDefault(c => c.StartsWith(_guard.Settings.TargetCountryCode, StringComparison.OrdinalIgnoreCase))
                          ?? Countries[0];
        EventFilters = ["Все", "vpn", "launch", "geo", "remind", "audit", "error"];

        LoadFromSettings(_guard.Settings);
        _guard.StateChanged += () => System.Windows.Application.Current?.Dispatcher.Invoke(PullState);
        PullState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProviderRow> Providers { get; } = [];
    public ObservableCollection<ClaudeRow> ClaudeProcesses { get; } = [];
    public ObservableCollection<JournalEntry> FilteredEvents { get; } = [];
    public ObservableCollection<HygieneTip> HygieneTips { get; } = [];
    public ObservableCollection<string> Recommendations { get; } = [];
    public ObservableCollection<double> SparkPoints { get; } = [];
    public List<string> Countries { get; }
    public List<string> EventFilters { get; }

    public ICommand SaveCommand { get; }
    public ICommand DisableClaudeAutostartCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand CheckVpnCommand { get; }
    public ICommand AllowLaunchCommand { get; }
    public ICommand RunAuditCommand { get; }
    public ICommand ClearEventsCommand { get; }

    public string SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!Set(ref _selectedNav, value)) return;
            OnPropertyChanged(nameof(IsHome));
            OnPropertyChanged(nameof(IsMonitoring));
            OnPropertyChanged(nameof(IsVpn));
            OnPropertyChanged(nameof(IsSecurity));
            OnPropertyChanged(nameof(IsJournal));
            OnPropertyChanged(nameof(IsSettings));
            OnPropertyChanged(nameof(IsAbout));
        }
    }

    public bool IsHome => SelectedNav == "home";
    public bool IsMonitoring => SelectedNav == "monitoring";
    public bool IsVpn => SelectedNav == "vpn";
    public bool IsSecurity => SelectedNav == "security";
    public bool IsJournal => SelectedNav == "journal";
    public bool IsSettings => SelectedNav == "settings";
    public bool IsAbout => SelectedNav == "about";

    public string StatusSummary { get => _statusSummary; set => Set(ref _statusSummary, value); }
    public string Reminder { get => _reminder; set => Set(ref _reminder, value); }
    public string ProtectionTitle { get => _protectionTitle; set => Set(ref _protectionTitle, value); }
    public string VpnTitle { get => _vpnTitle; set => Set(ref _vpnTitle, value); }
    public string ClaudeTitle { get => _claudeTitle; set => Set(ref _claudeTitle, value); }
    public string ClaudeSubtitle { get => _claudeSubtitle; set => Set(ref _claudeSubtitle, value); }
    public string VpnIp { get => _vpnIp; set => Set(ref _vpnIp, value); }
    public string VpnCountry { get => _vpnCountry; set => Set(ref _vpnCountry, value); }
    public string VpnLatency { get => _vpnLatency; set => Set(ref _vpnLatency, value); }
    public string UptimeText { get => _uptimeText; set => Set(ref _uptimeText, value); }
    public string GlobalStatus { get => _globalStatus; set => Set(ref _globalStatus, value); }
    public string SystemStatusTitle { get => _systemStatusTitle; set => Set(ref _systemStatusTitle, value); }
    public string SystemStatusSub { get => _systemStatusSub; set => Set(ref _systemStatusSub, value); }
    public string AuditTime { get => _auditTime; set => Set(ref _auditTime, value); }
    public string AuditDetail { get => _auditDetail; set => Set(ref _auditDetail, value); }
    public bool AuditOk { get => _auditOk; set => Set(ref _auditOk, value); }
    public bool VpnUp { get => _vpnUp; set => Set(ref _vpnUp, value); }
    public bool ClaudeAllowed { get => _claudeAllowed; set { if (Set(ref _claudeAllowed, value)) OnPropertyChanged(nameof(ClaudeCardBrush)); } }
    public bool GuardEnabled { get => _guardEnabled; set => Set(ref _guardEnabled, value); }
    public bool StrictLocation { get => _strictLocation; set => Set(ref _strictLocation, value); }
    public bool KillMode { get => _killMode; set => Set(ref _killMode, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public int AccountRating { get => _accountRating; set { if (Set(ref _accountRating, value)) OnPropertyChanged(nameof(RatingDashArray)); } }
    public string BlockedStartsText => _guard.BlockedStarts.ToString(CultureInfo.InvariantCulture);
    public string LaunchAttemptsText => _guard.LaunchAttempts.ToString(CultureInfo.InvariantCulture);
    public string BlockedDeltaText => _guard.BlockedStarts == 0 ? "" : $"+{_guard.BlockedStarts * 5}%";
    public string AttemptsDeltaText => _guard.LaunchAttempts == 0 ? "" : $"+{_guard.LaunchAttempts * 3}%";
    public string AutostartFindings { get => _autostartFindings; set => Set(ref _autostartFindings, value); }
    public string EventFilter
    {
        get => _eventFilter;
        set
        {
            if (Set(ref _eventFilter, value))
                RefreshFilteredEvents();
        }
    }

    public string SelectedCountry
    {
        get => $"{_targetCode} — {_targetLabel}";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var parts = value.Split('—', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) return;
            _targetCode = parts[0];
            _targetLabel = parts[1];
            OnPropertyChanged();
        }
    }

    public DoubleCollection RatingDashArray
    {
        get
        {
            // Ellipse circumference in dash units ≈ 2πr / strokeThickness; r≈46, stroke=10 → ~29
            const double circ = 28.8;
            var filled = circ * AccountRating / 100.0;
            return [filled, circ];
        }
    }

    public System.Windows.Media.Brush ClaudeCardBrush => ClaudeAllowed
        ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["GreenGlowBrush"]
        : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["RedGlowBrush"];

    private void LoadFromSettings(AppSettings s)
    {
        GuardEnabled = s.GuardEnabled;
        StrictLocation = s.StrictLocation;
        KillMode = s.KillInsteadOfSuspend;
        StartWithWindows = s.StartWithWindows;
        _targetCode = s.TargetCountryCode;
        _targetLabel = s.TargetLocationLabel;
        OnPropertyChanged(nameof(SelectedCountry));
    }

    private void PullState()
    {
        StatusSummary = _guard.StatusSummary;
        Reminder = _guard.ReminderText;
        ClaudeAllowed = _guard.ClaudeAllowed;
        AccountRating = _guard.AccountRating;

        var uptime = DateTimeOffset.Now - _guard.StartedAt;
        UptimeText = uptime.ToString(@"hh\:mm\:ss");

        var t = _guard.LastTunnelStatus;
        VpnUp = t?.State == TunnelState.Up;
        VpnTitle = VpnUp ? "VPN подключён" : "VPN отключён";
        ProtectionTitle = !_guard.Settings.GuardEnabled
            ? "Защита выключена"
            : VpnUp && ClaudeAllowed
                ? "Защита включена"
                : "Защита активна";

        ClaudeTitle = ClaudeAllowed ? "Claude Code: Разрешён" : "Claude Code: Заблокирован";
        ClaudeSubtitle = ClaudeAllowed
            ? (_guard.TemporaryAllowActive
                ? "Временный доступ активен (15 мин override)."
                : "Запуск разрешён — туннель и локация в норме.")
            : "Запуск Claude Code заблокирован до восстановления VPN / локации.";

        var g = _guard.LastGeo;
        VpnIp = g?.Ip ?? "—";
        VpnCountry = g is { Ok: true } ? $"{CountryFlag(g.CountryCode)} {g.CountryName}" : "Ожидание geo…";
        VpnLatency = _guard.LastLatencyMs is long ms ? $"{ms} мс" : "—";

        GlobalStatus = _guard.Settings.GuardEnabled ? (ClaudeAllowed || VpnUp ? "Активен" : "Блокировка") : "Пауза";
        SystemStatusTitle = _guard.Settings.GuardEnabled ? "Система активна" : "Защита выключена";
        SystemStatusSub = _guard.Settings.GuardEnabled
            ? (VpnUp ? "Все защиты работают" : "Ожидание VPN")
            : "Claude не ограничивается";

        AuditTime = _guard.LastAuditAt?.ToString("HH:mm:ss") ?? "—";
        AuditDetail = _guard.LastAuditDetail;
        AuditOk = _guard.LastAuditOk;

        ClaudeProcesses.Clear();
        foreach (var p in _guard.ClaudeProcesses)
            ClaudeProcesses.Add(new ClaudeRow(p.Id, p.Kind, p.Suspended ? "приостановлен" : "работает"));

        if (t is not null)
        {
            foreach (var row in Providers)
            {
                var match = t.Providers.FirstOrDefault(p => p.Id == row.Id);
                if (match is null) continue;
                row.Healthy = match.Healthy;
                row.Detail = match.Detail;
            }
        }

        UpdateSpark();
        RefreshFilteredEvents();

        var hits = AutostartManager.FindClaudeAutostartEntries();
        AutostartFindings = hits.Count == 0
            ? "Автозапуск Claude в Run не найден."
            : string.Join(Environment.NewLine, hits);

        OnPropertyChanged(nameof(BlockedStartsText));
        OnPropertyChanged(nameof(LaunchAttemptsText));
        OnPropertyChanged(nameof(BlockedDeltaText));
        OnPropertyChanged(nameof(AttemptsDeltaText));
        OnPropertyChanged(nameof(AutostartFindings));
    }

    private void UpdateSpark()
    {
        var seed = Math.Max(3, _guard.BlockedStarts + _guard.LaunchAttempts + (VpnUp ? 5 : 1));
        SparkPoints.Clear();
        var rnd = new Random(seed + DateTime.Now.Minute);
        for (var i = 0; i < 16; i++)
            SparkPoints.Add(10 + rnd.NextDouble() * 40 + (VpnUp ? 15 : 0));
    }

    private void RefreshFilteredEvents()
    {
        FilteredEvents.Clear();
        IEnumerable<JournalEntry> q = _guard.Journal.Entries;
        if (!string.Equals(EventFilter, "Все", StringComparison.OrdinalIgnoreCase))
            q = q.Where(e => e.Kind.Equals(EventFilter, StringComparison.OrdinalIgnoreCase));
        foreach (var e in q.Take(40))
            FilteredEvents.Add(e);
    }

    private void RefreshProviders()
    {
        Providers.Clear();
        foreach (var p in GuardService.CreateProviders())
        {
            Providers.Add(new ProviderRow
            {
                Id = p.Id,
                Name = p.DisplayName,
                Enabled = _guard.Settings.IsProviderEnabled(p.Id)
            });
        }
    }

    private void Save()
    {
        var s = _guard.Settings;
        s.GuardEnabled = GuardEnabled;
        s.StrictLocation = StrictLocation;
        s.KillInsteadOfSuspend = KillMode;
        s.StartWithWindows = StartWithWindows;
        s.TargetCountryCode = _targetCode;
        s.TargetLocationLabel = _targetLabel;
        foreach (var row in Providers)
            s.EnabledProviders[row.Id] = row.Enabled;
        _guard.UpdateSettings(s);
    }

    private void DisableClaudeAutostart()
    {
        var n = AutostartManager.DisableClaudeAutostartEntries();
        _guard.Journal.Add("autostart", $"Удалено записей автозапуска Claude: {n}");
        PullState();
    }

    private static string CountryFlag(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 2) return "🌐";
        code = code.ToUpperInvariant();
        return char.ConvertFromUtf32(0x1F1E6 + (code[0] - 'A')) +
               char.ConvertFromUtf32(0x1F1E6 + (code[1] - 'A'));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ProviderRow : INotifyPropertyChanged
{
    private bool _enabled;
    private bool _healthy;
    private string _detail = "";

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } }
    public bool Healthy { get => _healthy; set { _healthy = value; PropertyChanged?.Invoke(this, new(nameof(Healthy))); PropertyChanged?.Invoke(this, new(nameof(StatusText))); } }
    public string Detail { get => _detail; set { _detail = value; PropertyChanged?.Invoke(this, new(nameof(Detail))); PropertyChanged?.Invoke(this, new(nameof(StatusText))); } }
    public string StatusText => !Enabled ? "выкл" : Healthy ? $"онлайн · {Detail}" : Detail;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record ClaudeRow(int Pid, string Kind, string State);

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

public sealed class RelayCommand<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter is T t ? t : default);
}

public sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
{
    private bool _busy;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy;
    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
