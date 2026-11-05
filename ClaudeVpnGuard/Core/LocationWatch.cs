using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeVpnGuard.Core;

public sealed class GeoResult
{
    public bool Ok { get; init; }
    public string? Ip { get; init; }
    public string? CountryCode { get; init; }
    public string? CountryName { get; init; }
    public string? Error { get; init; }
    public string? Source { get; init; }
    /// <summary>How many geo providers agreed on the winning country.</summary>
    public int AgreementCount { get; init; }
    /// <summary>Total geo providers that returned a country sample.</summary>
    public int SampleCount { get; init; }
}

public sealed class LocationWatch : IDisposable
{
    private static readonly Dictionary<string, string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NL"] = "Netherlands",
        ["DE"] = "Germany",
        ["US"] = "United States",
        ["GB"] = "United Kingdom",
        ["FR"] = "France",
        ["FI"] = "Finland",
        ["SE"] = "Sweden",
        ["CH"] = "Switzerland",
        ["PL"] = "Poland",
        ["TR"] = "Turkey",
        ["JP"] = "Japan",
        ["SG"] = "Singapore",
        ["RU"] = "Russia",
        ["UA"] = "Ukraine",
        ["EE"] = "Estonia",
        ["LV"] = "Latvia",
        ["LT"] = "Lithuania",
        ["CZ"] = "Czechia",
        ["AT"] = "Austria",
        ["BE"] = "Belgium",
        ["IE"] = "Ireland",
        ["IT"] = "Italy",
        ["ES"] = "Spain",
        ["PT"] = "Portugal",
        ["NO"] = "Norway",
        ["DK"] = "Denmark",
        ["CA"] = "Canada",
        ["AU"] = "Australia",
        ["HK"] = "Hong Kong",
        ["KR"] = "South Korea",
        ["IN"] = "India",
        ["BR"] = "Brazil",
    };

    private readonly HttpClient _http;
    private DateTimeOffset _lastReminder = DateTimeOffset.MinValue;
    private string? _lastCountry;
    private string? _lastIp;

    public GeoResult? LastResult { get; private set; }
    public string? LastCountryCode => LastResult?.CountryCode;
    public string? LastIp => LastResult?.Ip;

    public LocationWatch()
    {
        // Prefer IPv4: users and VPN panels almost always check the v4 exit,
        // while SocketsHttpHandler may otherwise pick a differently-geolocated v6.
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;
                IPAddress[] addresses;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
                    if (addresses.Length == 0)
                        addresses = await Dns.GetHostAddressesAsync(host, ct);
                }
                catch
                {
                    addresses = await Dns.GetHostAddressesAsync(host, ct);
                }

                var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? addresses.FirstOrDefault()
                         ?? throw new HttpRequestException($"No DNS address for {host}");

                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "ClaudeVpnGuard/2.0 (+https://github.com/switzard/claude-vpn-guard)");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
    }

    public async Task<GeoResult> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var samples = new ConcurrentDictionary<string, (string Source, string? Ip, string? Code, string? Name)>(
                StringComparer.OrdinalIgnoreCase);

            await Task.WhenAll(
                TryCloudflareAsync(samples, ct),
                TryIpWhoAsync(samples, ct),
                TryIpApiAsync(samples, ct),
                TryTwoIpAsync(samples, ct),
                TryIpInfoIoAsync(samples, ct));

            if (samples.IsEmpty)
            {
                // Do not leak a previous country into a failed result — UI was treating
                // CountryCode as "current" even when Ok=false.
                LastResult = new GeoResult { Ok = false, Error = "all geo providers failed" };
                return LastResult;
            }

            var ip = PickIp(samples.Values);
            var (code, name, source, agree, sampleCount) = PickCountry(samples.Values);

            if (string.IsNullOrWhiteSpace(code))
            {
                LastResult = new GeoResult
                {
                    Ok = false,
                    Ip = ip,
                    Error = "geo providers returned no country",
                    Source = source,
                    AgreementCount = agree,
                    SampleCount = sampleCount
                };
                return LastResult;
            }

            name ??= CountryNames.GetValueOrDefault(code.ToUpperInvariant()) ?? code;

            LastResult = new GeoResult
            {
                Ok = true,
                Ip = ip,
                CountryCode = code.ToUpperInvariant(),
                CountryName = name,
                Source = source,
                AgreementCount = agree,
                SampleCount = sampleCount
            };

            _lastCountry = LastResult.CountryCode;
            _lastIp = LastResult.Ip;
            return LastResult;
        }
        catch (Exception ex)
        {
            LastResult = new GeoResult { Ok = false, Error = ex.Message };
            return LastResult;
        }
    }

    private async Task TryCloudflareAsync(
        ConcurrentDictionary<string, (string Source, string? Ip, string? Code, string? Name)> bag,
        CancellationToken ct)
    {
        try
        {
            // Cloudflare reports the connecting client's country — closest to what
            // CDNs / many services will see for this exit.
            using var response = await _http.GetAsync("https://cloudflare.com/cdn-cgi/trace", ct);
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(ct);
            var ip = MatchTrace(text, "ip");
            var loc = MatchTrace(text, "loc");
            if (string.IsNullOrWhiteSpace(loc) || loc is "XX" or "T1")
                return;

            var code = loc.Trim().ToUpperInvariant();
            bag["cloudflare"] = ("cloudflare", ip, code, CountryNames.GetValueOrDefault(code));
        }
        catch
        {
            /* provider optional */
        }
    }

    private async Task TryIpWhoAsync(
        ConcurrentDictionary<string, (string Source, string? Ip, string? Code, string? Name)> bag,
        CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("https://ipwho.is/", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                return;

            var code = root.TryGetProperty("country_code", out var cc) ? cc.GetString() : null;
            var name = root.TryGetProperty("country", out var cn) ? cn.GetString() : null;
            var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(code)) return;

            bag["ipwho"] = ("ipwho.is", ip, code.ToUpperInvariant(), name);
        }
        catch
        {
            /* provider optional */
        }
    }

    private async Task TryIpApiAsync(
        ConcurrentDictionary<string, (string Source, string? Ip, string? Code, string? Name)> bag,
        CancellationToken ct)
    {
        try
        {
            // Prefer HTTPS; free tier may refuse — failure is OK (other providers remain).
            using var response = await _http.GetAsync(
                "https://ip-api.com/json/?fields=status,country,countryCode,query", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var status) &&
                !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                return;

            var code = root.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null;
            var name = root.TryGetProperty("country", out var cn) ? cn.GetString() : null;
            var ip = root.TryGetProperty("query", out var ipEl) ? ipEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(code)) return;

            bag["ip-api"] = ("ip-api.com", ip, code.ToUpperInvariant(), name);
        }
        catch
        {
            /* provider optional */
        }
    }

    private async Task TryTwoIpAsync(
        ConcurrentDictionary<string, (string Source, string? Ip, string? Code, string? Name)> bag,
        CancellationToken ct)
    {
        try
        {
            // 2ip.ru JSON — owner reference for correct geo display.
            using var response = await _http.GetAsync("https://api.2ip.ua/geo.json", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var code = root.TryGetProperty("country_code", out var cc) ? cc.GetString()
                : root.TryGetProperty("countryCode", out var cc2) ? cc2.GetString() : null;
            var name = root.TryGetProperty("country", out var cn) ? cn.GetString()
                : root.TryGetProperty("country_name", out var cn2) ? cn2.GetString() : null;
            var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(ip))
                return;
            if (string.IsNullOrWhiteSpace(code)) return;
            bag["2ip"] = ("2ip.ua", ip, code.ToUpperInvariant(), name);
        }
        catch
        {
            try
            {
                using var response = await _http.GetAsync("https://api.2ip.ru/geo.json", ct);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;
                var code = root.TryGetProperty("country_code", out var cc) ? cc.GetString() : null;
                var name = root.TryGetProperty("country", out var cn) ? cn.GetString() : null;
                var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(code)) return;
                bag["2ip"] = ("2ip.ru", ip, code.ToUpperInvariant(), name);
            }
            catch
            {
                /* provider optional */
            }
        }
    }

    private async Task TryIpInfoIoAsync(
        ConcurrentDictionary<string, (string Source, string? Ip, string? Code, string? Name)> bag,
        CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("https://ipinfo.io/json", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var code = root.TryGetProperty("country", out var cc) ? cc.GetString() : null;
            var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(code)) return;
            var upper = code.ToUpperInvariant();
            bag["ipinfo"] = ("ipinfo.io", ip, upper, CountryNames.GetValueOrDefault(upper));
        }
        catch
        {
            /* provider optional */
        }
    }

    private static string? MatchTrace(string text, string key)
    {
        var m = Regex.Match(text, $@"(?:^|\n){Regex.Escape(key)}=([^\r\n]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? PickIp(IEnumerable<(string Source, string? Ip, string? Code, string? Name)> samples)
    {
        var ips = samples
            .Select(s => s.Ip)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Cast<string>()
            .ToList();
        if (ips.Count == 0) return null;

        // Prefer the IP that most providers agree on; break ties with Cloudflare order.
        return ips
            .GroupBy(ip => ip, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => Preference(samples.FirstOrDefault(s =>
                string.Equals(s.Ip, g.Key, StringComparison.OrdinalIgnoreCase)).Source))
            .Select(g => g.Key)
            .First();
    }

    private static (string? Code, string? Name, string? Source, int Agree, int Samples) PickCountry(
        IEnumerable<(string Source, string? Ip, string? Code, string? Name)> samples)
    {
        var list = samples.Where(s => !string.IsNullOrWhiteSpace(s.Code)).ToList();
        if (list.Count == 0) return (null, null, null, 0, 0);

        // Majority vote on ISO country code. On a tie, prefer Cloudflare (CDN view).
        var winner = list
            .GroupBy(s => s.Code!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(x => Preference(x.Source)))
            .First();

        // Require ≥2 agreeing samples when we have ≥2 successful geo replies.
        if (list.Count >= 2 && winner.Count() < 2)
            return (null, null, null, winner.Count(), list.Count);

        var best = winner.OrderBy(x => Preference(x.Source)).First();
        var sources = string.Join("+", winner.Select(x => x.Source).Distinct());
        var name = winner.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                   ?? CountryNames.GetValueOrDefault(best.Code!);
        return (best.Code, name, sources, winner.Count(), list.Count);
    }

    private static int Preference(string? source) => source switch
    {
        "cloudflare" => 0,
        "ipwho.is" => 1,
        "ipinfo.io" => 2,
        "2ip.ua" => 3,
        "2ip.ru" => 3,
        "ip-api.com" => 4,
        _ => 9
    };

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
        // Wrong-exit nags only in strict mode (soft mode tolerates other countries).
        if (settings.StrictLocation && LastResult is { Ok: true } && !MatchesTarget(settings))
            return true;
        return false;
    }

    public void MarkReminded() => _lastReminder = DateTimeOffset.Now;

    public bool CountryChanged(string? previous) =>
        !string.IsNullOrEmpty(LastCountryCode) &&
        !string.Equals(previous, LastCountryCode, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _http.Dispose();
}
