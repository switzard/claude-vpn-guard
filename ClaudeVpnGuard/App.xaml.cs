using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ClaudeVpnGuard.Core;
using Application = System.Windows.Application;

namespace ClaudeVpnGuard;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private GuardService? _guard;
    private MainWindow? _window;
    private Icon? _currentIcon;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        UpdateTrayIcon(true, false, false);

        _tray.DoubleClick += (_, _) => ShowMain();
        _guard.StateChanged += OnGuardStateChanged;
        _guard.Start();
        ShowMain();
    }

    private void OnGuardStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_guard is null || _tray is null) return;
            var vpnUp = _guard.LastTunnelStatus?.State == Tunnel.TunnelState.Up;
            UpdateTrayIcon(_guard.Settings.GuardEnabled, _guard.ClaudeAllowed, vpnUp);
            _tray.Text = Truncate($"Claude Code Guardian — {_guard.StatusSummary}");
        });
    }

    private void UpdateTrayIcon(bool guardEnabled, bool allowed, bool vpnUp)
    {
        if (_tray is null) return;
        var old = _currentIcon;
        _currentIcon = TrayIconFactory.Create(TrayIconFactory.ColorFor(guardEnabled, allowed, vpnUp));
        _tray.Icon = _currentIcon;
        old?.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        return menu;
    }

    private void ShowMain()
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

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _guard?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _currentIcon?.Dispose();
        Shutdown();
    }

    private static string Truncate(string text) =>
        text.Length <= 63 ? text : text[..60] + "…";
}
