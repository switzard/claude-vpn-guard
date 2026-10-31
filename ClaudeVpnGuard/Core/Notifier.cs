using System.IO;
using System.Media;
using System.Windows.Forms;

namespace ClaudeVpnGuard.Core;

public sealed class Notifier
{
    private NotifyIcon? _icon;
    private Func<AppSettings>? _settingsGetter;

    public void Attach(NotifyIcon icon) => _icon = icon;

    public void BindSettings(Func<AppSettings> settings) => _settingsGetter = settings;

    public void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Warning)
    {
        if (_icon is null) return;
        try
        {
            _icon.BalloonTipTitle = title.Length > 63 ? title[..60] + "…" : title;
            _icon.BalloonTipText = text.Length > 250 ? text[..247] + "…" : text;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(6000);
            PlaySound();
        }
        catch
        {
            // some hosts suppress balloons
        }
    }

    private void PlaySound()
    {
        try
        {
            var s = _settingsGetter?.Invoke();
            if (s is null || !s.SoundEnabled) return;

            if (!string.IsNullOrWhiteSpace(s.CustomSoundPath) && File.Exists(s.CustomSoundPath))
            {
                using var player = new SoundPlayer(s.CustomSoundPath);
                player.Play();
                return;
            }

            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // ignore audio failures
        }
    }
}
