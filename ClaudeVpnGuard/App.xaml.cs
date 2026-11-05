using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ClaudeVpnGuard.Core;
using Application = System.Windows.Application;

namespace ClaudeVpnGuard;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private NotifyIcon? _tray;
    private GuardService? _guard;
    private MainWindow? _window;
    private HudWindow? _hud;
    private Icon? _currentIcon;
    private int _lastTrayColor = int.MinValue;
    private bool _exiting;
    /// <summary>User hid HUD via × — do not auto-reopen until explicit ShowHud / settings toggle.</summary>
    private bool _hudUserHidden;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\ClaudeVpnGuard.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Claude Code Guardian уже запущен.\nОткройте его из трея — второй экземпляр не нужен (он замораживает Claude повторно).",
                "Claude Code Guardian",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var journal = new EventJournal();
        var notifier = new Notifier();
        _guard = new GuardService(journal, notifier);

        _tray = new NotifyIcon
        {
            Text = "Claude Code Guardian",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        notifier.Attach(_tray);
        notifier.BindSettings(() => _guard!.Settings);
        notifier.BindToastHook((title, body, severity) =>
            WindowsToastService.Show(title, body, severity));
        UpdateTrayIcon(true, false, false);

        _tray.DoubleClick += (_, _) => ShowMain();
        _guard.StateChanged += OnGuardStateChanged;
        _guard.Start();
        ShowMain();
        if (_guard.Settings.HudEnabled)
            ShowHud();
    }

    private void OnGuardStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_guard is null || _tray is null) return;
            var vpnUp = _guard.LastTunnelStatus?.State == Tunnel.TunnelState.Up;
            UpdateTrayIcon(_guard.Settings.GuardEnabled, _guard.ClaudeAllowed, vpnUp);
            _tray.Text = Truncate($"Claude Code Guardian — {_guard.StatusSummary}");
            if (_hud is { IsVisible: true })
                _hud.Push();
            SyncHudVisibility();
        });
    }

    private void UpdateTrayIcon(bool guardEnabled, bool allowed, bool vpnUp)
    {
        if (_tray is null) return;
        var color = TrayIconFactory.ColorFor(guardEnabled, allowed, vpnUp);
        var key = color.ToArgb();
        if (key == _lastTrayColor && _currentIcon is not null)
            return;
        _lastTrayColor = key;
        var old = _currentIcon;
        _currentIcon = TrayIconFactory.Create(color);
        _tray.Icon = _currentIcon;
        old?.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RefreshTrayMenu(menu);
        menu.Items.Add("Открыть", null, (_, _) => ShowMain());
        menu.Items.Add("Показать HUD", null, (_, _) =>
        {
            if (_guard is null) return;
            var s = _guard.Settings;
            s.HudEnabled = true;
            // UpdateSettings → StateChanged → MainWindow PushState syncs General toggle (avoids autoSave wipe).
            _guard.UpdateSettings(s);
            _hudUserHidden = false;
            ShowHud();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Пауза Guard", null, (_, _) =>
        {
            if (_guard is null) return;
            _guard.SetGuardPaused(true);
        });
        menu.Items.Add("Возобновить Guard", null, (_, _) =>
        {
            if (_guard is null) return;
            _guard.SetGuardPaused(false);
        });
        menu.Items.Add("Разрешить Claude 15 мин · риск", null, (_, _) =>
            _guard?.AllowLaunchTemporarily(TimeSpan.FromMinutes(15)));
        menu.Items.Add("Отозвать временный доступ", null, (_, _) =>
            _guard?.RevokeTemporaryAllow());
        menu.Items.Add("Проверить VPN", null, async (_, _) =>
        {
            if (_guard is null) return;
            await _guard.CheckVpnAsync();
        });
        menu.Items.Add("Открыть Exit mismatch", null, (_, _) =>
        {
            ShowMain();
            if (_guard is null) return;
            var g = _guard.LastGeo;
            var mismatch = g is { Ok: true } &&
                           !string.Equals(g.CountryCode, _guard.Settings.TargetCountryCode,
                               StringComparison.OrdinalIgnoreCase);
            if (mismatch)
                _guard.Journal.Add("geo",
                    $"Exit mismatch: сейчас {g!.CountryCode}, цель {_guard.Settings.TargetCountryCode}");
            else
                _guard.Journal.Add("geo", "Exit совпадает с целью (или geo ещё нет)");
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        return menu;
    }

    private void RefreshTrayMenu(ContextMenuStrip menu)
    {
        if (_guard is null) return;
        var paused = !_guard.Settings.GuardEnabled;
        var allowLeft = _guard.TemporaryAllowRemainingSeconds;
        foreach (ToolStripItem item in menu.Items)
        {
            if (item.Text is null) continue;
            if (item.Text.StartsWith("Пауза Guard", StringComparison.Ordinal))
                item.Enabled = !paused;
            else if (item.Text.StartsWith("Возобновить Guard", StringComparison.Ordinal))
                item.Enabled = paused;
            else if (item.Text.StartsWith("Разрешить Claude", StringComparison.Ordinal))
                item.Text = allowLeft > 0
                    ? $"Разрешить Claude 15 мин · риск ({allowLeft / 60:D2}:{allowLeft % 60:D2})"
                    : "Разрешить Claude 15 мин · риск";
            else if (item.Text.StartsWith("Отозвать временный", StringComparison.Ordinal))
                item.Enabled = allowLeft > 0;
        }
    }

    public void ShowMain()
    {
        if (_guard is null) return;
        if (_window is null)
        {
            _window = new MainWindow(_guard);
            _window.Closing += (_, args) =>
            {
                if (_exiting) return;
                args.Cancel = true;
                _window.Hide();
            };
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            ClaudeVpnGuard.Core.NativeMethods.RestoreNative(hwnd);
        }
        else
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
    }

    public void ShowHud()
    {
        if (_guard is null) return;
        _hudUserHidden = false;
        if (_hud is null)
            _hud = new HudWindow(_guard, ShowMain);
        _hud.Show();
        _hud.Activate();
        _hud.Push();
    }

    public void HideHud() => _hud?.Hide();

    /// <summary>Hide from HUD × — keeps HudEnabled so settings stay ON.</summary>
    public void HideHudKeepEnabled()
    {
        _hudUserHidden = true;
        HideHud();
    }

    private void SyncHudVisibility()
    {
        if (_guard is null) return;
        if (!_guard.Settings.HudEnabled)
        {
            _hudUserHidden = false;
            HideHud();
            return;
        }
        // Enabled but user closed with × — leave hidden until tray/settings/Open HUD
        if (_hudUserHidden) return;
        if (_hud is null || !_hud.IsVisible)
            ShowHud();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _guard?.Dispose();
        if (_hud is not null)
        {
            try { _hud.ForceClose(); } catch { /* ignore */ }
            _hud = null;
        }
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _currentIcon?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { /* ignore */ }
        _singleInstance?.Dispose();
        _singleInstance = null;
        Shutdown();
    }

    private static string Truncate(string text) =>
        text.Length <= 63 ? text : text[..60] + "…";
}
