using System.IO;
using System.Media;
using System.Windows.Forms;

namespace ClaudeVpnGuard.Core;

public sealed class Notifier
{
    private NotifyIcon? _icon;
    private Func<AppSettings>? _settingsGetter;
    private Func<string, string, string, bool>? _toastHook;

    public void Attach(NotifyIcon icon) => _icon = icon;

    public void BindSettings(Func<AppSettings> settings) => _settingsGetter = settings;

    /// <summary>Optional hook for modern Windows App notifications (Action Center). Return true on delivery.</summary>
    public void BindToastHook(Func<string, string, string, bool> hook) => _toastHook = hook;

    public void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Warning)
    {
        var preferToastOnly = _settingsGetter?.Invoke()?.PreferToastOnly != false;
        var toastOk = false;
        try
        {
            if (_toastHook is not null)
                toastOk = _toastHook(title, text, icon.ToString());
        }
        catch
        {
            toastOk = false;
        }

        PlaySound();

        // Errors always keep balloon fallback — toast "ok" handoff can still be invisible.
        if (preferToastOnly && toastOk && icon != ToolTipIcon.Error) return;
        if (_icon is null) return;
        try
        {
            _icon.BalloonTipTitle = title.Length > 63 ? title[..60] + "…" : title;
            _icon.BalloonTipText = text.Length > 250 ? text[..247] + "…" : text;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(6000);
        }
        catch
        {
            // some hosts suppress balloons
        }
    }

    public void PreviewSound() => PlaySound();

    private void PlaySound()
    {
        try
        {
            var s = _settingsGetter?.Invoke();
            if (s is null || !s.SoundEnabled) return;

            var path = ResolveSoundPath(s);
            if (path is null || !File.Exists(path))
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            var volume = Math.Clamp(s.SoundVolume, 0, 100) / 100f;
            if (volume <= 0.01f) return;

            var data = File.ReadAllBytes(path);
            var scaled = ScaleWavVolume(data, volume);
            using var ms = new MemoryStream(scaled);
            using var player = new SoundPlayer(ms);
            player.Play();
        }
        catch
        {
            try { SystemSounds.Asterisk.Play(); } catch { /* ignore */ }
        }
    }

    private static string? ResolveSoundPath(AppSettings s)
    {
        if (string.Equals(s.SoundPreset, "custom", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(s.CustomSoundPath) &&
            File.Exists(s.CustomSoundPath))
            return s.CustomSoundPath;

        if (!string.IsNullOrWhiteSpace(s.CustomSoundPath) &&
            string.Equals(s.SoundPreset, "custom", StringComparison.OrdinalIgnoreCase) == false &&
            File.Exists(s.CustomSoundPath) &&
            string.IsNullOrWhiteSpace(s.SoundPreset))
            return s.CustomSoundPath;

        var name = s.SoundPreset?.ToLowerInvariant() switch
        {
            "beep" => "beep.wav",
            "chime" => "chime.wav",
            "alert" => "alert.wav",
            "custom" => null,
            _ => "meme.wav"
        };
        if (name is null) return s.CustomSoundPath;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", name);
        return File.Exists(bundled) ? bundled : null;
    }

    private static byte[] ScaleWavVolume(byte[] wav, float volume)
    {
        if (volume >= 0.99f) return wav;
        if (wav.Length < 44) return wav;

        var copy = (byte[])wav.Clone();
        // Assume standard PCM 16-bit LE after 44-byte header (our bundled assets).
        var offset = 44;
        for (var i = offset; i + 1 < copy.Length; i += 2)
        {
            var sample = (short)(copy[i] | (copy[i + 1] << 8));
            var scaled = (short)Math.Clamp((int)(sample * volume), short.MinValue, short.MaxValue);
            copy[i] = (byte)(scaled & 0xFF);
            copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
        return copy;
    }
}
