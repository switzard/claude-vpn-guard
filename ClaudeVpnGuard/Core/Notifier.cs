using System.Windows.Forms;

namespace ClaudeVpnGuard.Core;

public sealed class Notifier
{
    private NotifyIcon? _icon;

    public void Attach(NotifyIcon icon) => _icon = icon;

    public void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Warning)
    {
        if (_icon is null) return;
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(6000);
        }
        catch
        {
            // some hosts suppress balloons
        }
    }
}
