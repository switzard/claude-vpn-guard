using System.Net.Http;
using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public sealed class GeoResult
{
    public bool Ok { get; init; }
    public string? Ip { get; init; }
    public string? CountryCode { get; init; }
    public string? CountryName { get; init; }
    public string? Error { get; init; }
}

public sealed class LocationWatch : IDisposable
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private DateTimeOffset _lastReminder = DateTimeOffset.MinValue;
    private string? _lastCountry;
    private string? _lastIp;

    public GeoResult? LastResult { get; private set; }
    public string? LastCountryCode => LastResult?.CountryCode;
    public string? LastIp => LastResult?.Ip;

    public async Task<GeoResult> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            // ipwho.is is HTTPS, no key, returns country_code
            using var response = await _http.GetAsync("https://ipwho.is/", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            {
                LastResult = new GeoResult { Ok = false, Error = "geo lookup failed" };
                return LastResult;
            }

            var code = root.TryGetProperty("country_code", out var cc) ? cc.GetString() : null;
            var name = root.TryGetProperty("country", out var cn) ? cn.GetString() : null;
            var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;

            LastResult = new GeoResult
            {
                Ok = !string.IsNullOrWhiteSpace(code),
                CountryCode = code?.ToUpperInvariant(),
                CountryName = name,
                Ip = ip
            };

            _lastCountry = LastResult.CountryCode;
            _lastIp = LastResult.Ip;
            return LastResult;
        }
        catch (Exception ex)
        {
            LastResult = new GeoResult { Ok = false, Error = ex.Message, Ip = _lastIp, CountryCode = _lastCountry };
            return LastResult;
        }
    }

    public bool MatchesTarget(AppSettings settings)
    {
        if (LastResult is null || !LastResult.Ok || string.IsNullOrWhiteSpace(LastResult.CountryCode))
            return false;

        return string.Equals(LastResult.CountryCode, settings.TargetCountryCode, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldRemind(AppSettings settings, bool vpnUp)
    {
        var due = DateTimeOffset.Now - _lastReminder >= TimeSpan.FromSeconds(Math.Max(30, settings.ReminderCooldownSeconds));
        if (!due) return false;

        if (!vpnUp) return true;
        if (!MatchesTarget(settings)) return true;
        return false;
    }

    public void MarkReminded() => _lastReminder = DateTimeOffset.Now;

    public bool CountryChanged(string? previous) =>
        !string.IsNullOrEmpty(LastCountryCode) &&
        !string.Equals(previous, LastCountryCode, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _http.Dispose();
}
