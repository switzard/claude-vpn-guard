using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public enum PassportState
{
    Broken = 0,
    Shifted = 1,
    Stable = 2
}

public sealed class PassportSnapshot
{
    public string Fingerprint { get; set; } = "";
    public PassportState State { get; set; } = PassportState.Broken;
    public string? Ip { get; set; }
    public string? CountryCode { get; set; }
    public int AgreementCount { get; set; }
    public int SampleCount { get; set; }
    public string? ProviderId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class PassportShift
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string FromFingerprint { get; set; } = "";
    public string ToFingerprint { get; set; } = "";
    public string Reason { get; set; } = "";
    public PassportState State { get; set; }
}

/// <summary>
/// Session continuity identity — not an IP log. Stable fingerprint of exit face.
/// </summary>
public sealed class SessionPassport
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly List<PassportShift> _shifts = [];
    private PassportSnapshot _current = new();
    private string? _anchorFingerprint;

    public PassportSnapshot Current
    {
        get { lock (_gate) return Clone(_current); }
    }

    public IReadOnlyList<PassportShift> RecentShifts
    {
        get { lock (_gate) return _shifts.Take(24).ToList(); }
    }

    public string FilePath => Path.Combine(SettingsStore.DirectoryPath, "passport-shifts.jsonl");

    public PassportSnapshot Observe(GeoResult? geo, string? providerId, bool tunnelUp)
    {
        lock (_gate)
        {
            // Tunnel down → Broken. Geo flap while tunnel still Up → keep last identity (avoid false Broken).
            if (!tunnelUp)
            {
                if (_current.State != PassportState.Broken)
                    RecordShift(_current.Fingerprint, "", "tunnel-down", PassportState.Broken);
                _current = new PassportSnapshot
                {
                    Fingerprint = "",
                    State = PassportState.Broken,
                    Ip = geo?.Ip,
                    CountryCode = geo?.CountryCode,
                    AgreementCount = geo?.AgreementCount ?? 0,
                    SampleCount = geo?.SampleCount ?? 0,
                    ProviderId = providerId,
                    UpdatedAt = DateTimeOffset.Now
                };
                return Clone(_current);
            }

            if (geo is not { Ok: true } || string.IsNullOrWhiteSpace(geo.Ip) ||
                string.IsNullOrWhiteSpace(geo.CountryCode))
            {
                if (!string.IsNullOrEmpty(_current.Fingerprint))
                {
                    _current.UpdatedAt = DateTimeOffset.Now;
                    return Clone(_current);
                }
                if (_current.State != PassportState.Broken)
                    RecordShift(_current.Fingerprint, "", "geo-unknown", PassportState.Broken);
                _current = new PassportSnapshot
                {
                    Fingerprint = "",
                    State = PassportState.Broken,
                    Ip = geo?.Ip,
                    CountryCode = geo?.CountryCode,
                    AgreementCount = geo?.AgreementCount ?? 0,
                    SampleCount = geo?.SampleCount ?? 0,
                    ProviderId = providerId,
                    UpdatedAt = DateTimeOffset.Now
                };
                return Clone(_current);
            }

            var fp = ComputeFingerprint(geo.Ip!, geo.CountryCode!, providerId);

            if (string.IsNullOrEmpty(_anchorFingerprint))
            {
                _anchorFingerprint = fp;
                _current = Build(fp, PassportState.Stable, geo, providerId);
                return Clone(_current);
            }

            if (string.Equals(fp, _current.Fingerprint, StringComparison.Ordinal))
            {
                _current.UpdatedAt = DateTimeOffset.Now;
                _current.AgreementCount = geo.AgreementCount;
                _current.SampleCount = geo.SampleCount;
                return Clone(_current);
            }

            var state = string.Equals(fp, _anchorFingerprint, StringComparison.Ordinal)
                ? PassportState.Stable
                : PassportState.Shifted;
            RecordShift(_current.Fingerprint, fp, "identity-change", state);
            if (state == PassportState.Stable)
                _anchorFingerprint = fp; // re-anchor when back to original
            _current = Build(fp, state, geo, providerId);
            return Clone(_current);
        }
    }

    public void Reanchor()
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(_current.Fingerprint)) return;
            _anchorFingerprint = _current.Fingerprint;
            _current.State = PassportState.Stable;
            _current.UpdatedAt = DateTimeOffset.Now;
            RecordShift(_current.Fingerprint, _current.Fingerprint, "reanchor", PassportState.Stable);
        }
    }

    public static string ComputeFingerprint(string ip, string country, string? providerId) =>
        ComputeFingerprint(ip, country, 0, 0, providerId);

    public static string ComputeFingerprint(
        string ip, string country, int agree, int samples, string? providerId)
    {
        // Identity = exit IP + country + provider only. Agreement counts flap between polls
        // and must not force false Shifted (agree/samples kept only for API compat / ignored).
        _ = agree;
        _ = samples;
        var raw = $"{ip.Trim()}|{country.Trim().ToUpperInvariant()}|{(providerId ?? "").Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..12];
    }

    private void RecordShift(string from, string to, string reason, PassportState state)
    {
        var s = new PassportShift
        {
            FromFingerprint = from,
            ToFingerprint = to,
            Reason = reason,
            State = state,
            Timestamp = DateTimeOffset.Now
        };
        _shifts.Insert(0, s);
        while (_shifts.Count > 40) _shifts.RemoveAt(_shifts.Count - 1);
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(s, Json) + Environment.NewLine);
        }
        catch { /* ignore */ }
    }

    private static PassportSnapshot Build(string fp, PassportState state, GeoResult geo, string? providerId) =>
        new()
        {
            Fingerprint = fp,
            State = state,
            Ip = geo.Ip,
            CountryCode = geo.CountryCode,
            AgreementCount = geo.AgreementCount,
            SampleCount = geo.SampleCount,
            ProviderId = providerId,
            UpdatedAt = DateTimeOffset.Now
        };

    private static PassportSnapshot Clone(PassportSnapshot s) => new()
    {
        Fingerprint = s.Fingerprint,
        State = s.State,
        Ip = s.Ip,
        CountryCode = s.CountryCode,
        AgreementCount = s.AgreementCount,
        SampleCount = s.SampleCount,
        ProviderId = s.ProviderId,
        UpdatedAt = s.UpdatedAt
    };
}
