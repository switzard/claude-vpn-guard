using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClaudeVpnGuard.Core;
using ClaudeVpnGuard.Hygiene;
using ClaudeVpnGuard.Tunnel;

namespace ClaudeVpnGuard.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly GuardService _guard;
    private string _statusSummary = "";
    private string _tunnelDetail = "";
    private string _geoDetail = "";
    private string _reminder = "";
    private string _claudeAllowed = "";
    private bool _guardEnabled = true;
    private bool _strictLocation = true;
    private bool _killMode;
    private bool _startWithWindows = true;
    private string _targetCode = "NL";
    private string _targetLabel = "Netherlands";

    public MainViewModel(GuardService guard)
    {
        _guard = guard;
        SaveCommand = new RelayCommand(Save);
        DisableClaudeAutostartCommand = new RelayCommand(DisableClaudeAutostart);
        RefreshProviders();
        foreach (var tip in AccountHygiene.Tips)
            HygieneTips.Add(tip);

        Countries = KnownCountries.All.Select(c => $"{c.Code} — {c.Label}").ToList();
        SelectedCountry = Countries.FirstOrDefault(c => c.StartsWith(_guard.Settings.TargetCountryCode, StringComparison.OrdinalIgnoreCase))
                          ?? Countries[0];

        LoadFromSettings(_guard.Settings);
        _guard.StateChanged += () =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(PullState);
        };
        PullState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProviderRow> Providers { get; } = [];
    public ObservableCollection<ClaudeRow> ClaudeProcesses { get; } = [];
    public ObservableCollection<JournalEntry> Events => _guard.Journal.Entries;
    public ObservableCollection<HygieneTip> HygieneTips { get; } = [];
    public List<string> Countries { get; }

    public ICommand SaveCommand { get; }
    public ICommand DisableClaudeAutostartCommand { get; }

    public string StatusSummary { get => _statusSummary; set => Set(ref _statusSummary, value); }
    public string TunnelDetail { get => _tunnelDetail; set => Set(ref _tunnelDetail, value); }
    public string GeoDetail { get => _geoDetail; set => Set(ref _geoDetail, value); }
    public string Reminder { get => _reminder; set => Set(ref _reminder, value); }
    public string ClaudeAllowedText { get => _claudeAllowed; set => Set(ref _claudeAllowed, value); }
    public bool GuardEnabled { get => _guardEnabled; set => Set(ref _guardEnabled, value); }
    public bool StrictLocation { get => _strictLocation; set => Set(ref _strictLocation, value); }
    public bool KillMode { get => _killMode; set => Set(ref _killMode, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public string SelectedCountry
    {
        get => $"{_targetCode} — {_targetLabel}";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var parts = value.Split('—', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                _targetCode = parts[0];
                _targetLabel = parts[1];
                OnPropertyChanged();
            }
        }
    }

    public string AutostartFindings { get; private set; } = "";

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
        ClaudeAllowedText = _guard.ClaudeAllowed ? "Claude allowed" : "Claude blocked / suspended";

        var t = _guard.LastTunnelStatus;
        TunnelDetail = t is null
            ? "…"
            : t.State == TunnelState.Up
                ? $"{t.ActiveProviderName}: {t.Detail}"
                : "No healthy tunnel";

        var g = _guard.LastGeo;
        GeoDetail = g is null
            ? "Geo: waiting…"
            : g.Ok
                ? $"Exit: {g.CountryName} ({g.CountryCode}) · {g.Ip}"
                : $"Geo error: {g.Error}";

        ClaudeProcesses.Clear();
        foreach (var p in _guard.ClaudeProcesses)
            ClaudeProcesses.Add(new ClaudeRow(p.Id, p.Kind, p.Suspended ? "suspended" : "running"));

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

        var hits = AutostartManager.FindClaudeAutostartEntries();
        AutostartFindings = hits.Count == 0
            ? "No Claude Run-key autostart entries found."
            : string.Join(Environment.NewLine, hits);
        OnPropertyChanged(nameof(AutostartFindings));
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
        _guard.Journal.Add("autostart", $"Removed {n} Claude Run-key entries");
        PullState();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
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
    public string StatusText => !Enabled ? "off" : Healthy ? $"up · {Detail}" : Detail;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record ClaudeRow(int Pid, string Kind, string State);

public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
