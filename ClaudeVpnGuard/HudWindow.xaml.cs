using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ClaudeVpnGuard.Core;
using Microsoft.Web.WebView2.Core;

namespace ClaudeVpnGuard;

public partial class HudWindow : Window
{
    private readonly GuardService _guard;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private bool _ready;
    private bool _forceClose;
    private IntPtr _hwnd;
    private Action? _openMain;

    public HudWindow(GuardService guard, Action openMain)
    {
        InitializeComponent();
        _guard = guard;
        _openMain = openMain;
        Loaded += async (_, _) => await InitAsync();
        _guard.StateChanged += OnChanged;
        Closing += (_, e) =>
        {
            if (_forceClose) return;
            // Hide instead of destroy; mark user-hidden so SyncHudVisibility won't reopen
            e.Cancel = true;
            (System.Windows.Application.Current as App)?.HideHudKeepEnabled();
        };
    }

    public void ForceClose()
    {
        _forceClose = true;
        _guard.StateChanged -= OnChanged;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableRoundedCorners(_hwnd);
        Left = SystemParameters.WorkArea.Right - Width - 16;
        Top = SystemParameters.WorkArea.Top + 16;
    }

    private async Task InitAsync()
    {
        var uiDir = Path.Combine(AppContext.BaseDirectory, "UI");
        var hud = Path.Combine(uiDir, "hud.html");
        if (!File.Exists(hud)) return;

        var env = await CoreWebView2Environment.CreateAsync();
        await WebView.EnsureCoreWebView2Async(env);
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        WebView.CoreWebView2.WebMessageReceived += OnMessage;
        WebView.CoreWebView2.Navigate(new Uri(hud).AbsoluteUri);
    }

    private void OnChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_ready && IsVisible) Push();
        });
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                    Push();
                    break;
                case "checkVpn":
                    _ = _guard.CheckVpnAsync();
                    break;
                case "allowLaunch":
                    _guard.AllowLaunchTemporarily(TimeSpan.FromMinutes(15));
                    break;
                case "revokeAllow":
                    _guard.RevokeTemporaryAllow();
                    Push();
                    break;
                case "openMain":
                    _openMain?.Invoke();
                    break;
                case "ackLatest":
                {
                    var open = IncidentStore.LatestOpen();
                    if (open is not null) _guard.AcknowledgeIncident(open.Id);
                    Push();
                    break;
                }
                case "hudDrag":
                    if (_hwnd == IntPtr.Zero)
                        _hwnd = new WindowInteropHelper(this).Handle;
                    NativeMethods.StartWindowDrag(_hwnd);
                    break;
                case "hudClose":
                case "hudHide":
                    // × = hide window only; HudEnabled stays so settings toggle stays ON
                    (System.Windows.Application.Current as App)?.HideHudKeepEnabled();
                    _guard.Journal.Add("settings", "Guard HUD скрыт (окно)");
                    break;
            }
        }
        catch
        {
            /* ignore */
        }
    }

    public void Push()
    {
        if (WebView.CoreWebView2 is null) return;
        var t = _guard.LastTunnelStatus;
        var p = _guard.LastPassport;
        var open = IncidentStore.LatestOpen();
        // Tight chrome when idle; grow only for the incident strip — no dead bottom void.
        Height = open is null ? 214 : 268;
        var payload = new
        {
            passportState = p.State.ToString(),
            fingerprint = p.Fingerprint,
            vpnUp = t?.State == Tunnel.TunnelState.Up,
            vpnDegraded = t?.State == Tunnel.TunnelState.Degraded,
            claudeAllowed = _guard.ClaudeAllowed,
            guardEnabled = _guard.Settings.GuardEnabled,
            allowSeconds = _guard.TemporaryAllowRemainingSeconds,
            summary = _guard.StatusSummary,
            openIncident = open is null ? null : new { open.Id, open.Summary, open.Kind },
            country = p.CountryCode ?? "",
            lang = string.Equals(_guard.Settings.UiLanguage, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru"
        };
        WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, _json));
    }
}
