using System.IO;
using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public sealed class GuardIncident
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "vpn-drop"; // vpn-drop | geo-break
    public bool Open { get; set; } = true;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string Summary { get; set; } = "";
    public string? PassportBefore { get; set; }
    public string? PassportAfter { get; set; }
    public string? ProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? AdapterName { get; set; }
    public string? ExitIp { get; set; }
    public string? ExitCountry { get; set; }
    public string? GeoSource { get; set; }
    public int GeoAgreement { get; set; }
    public int GeoSamples { get; set; }
    public List<string> AdaptersUp { get; set; } = [];
    public List<string> ClaudeProcesses { get; set; } = [];
    public string? Detail { get; set; }
}

public static class IncidentStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly object Gate = new();
    private static readonly List<GuardIncident> Memory = [];
    private static bool _loaded;

    public static string FilePath =>
        Path.Combine(SettingsStore.DirectoryPath, "incidents.jsonl");

    /// <returns>Incident and whether a new row was written (false = existing open same-kind reused).</returns>
    public static (GuardIncident Incident, bool IsNew) AppendResult(GuardIncident incident)
    {
        lock (Gate)
        {
            EnsureLoaded();
            // One open pack per kind until ack — not just 30s (flap would otherwise stack opens).
            var dup = Memory.FirstOrDefault(i =>
                i.Open &&
                string.Equals(i.Kind, incident.Kind, StringComparison.OrdinalIgnoreCase));
            if (dup is not null) return (dup, false);

            Memory.Insert(0, incident);
            while (Memory.Count > 100) Memory.RemoveAt(Memory.Count - 1);
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(incident, Json) + Environment.NewLine);
            }
            catch { /* ignore */ }
            return (incident, true);
        }
    }

    public static GuardIncident Append(GuardIncident incident) => AppendResult(incident).Incident;

    public static IReadOnlyList<GuardIncident> Recent(int take = 40)
    {
        lock (Gate)
        {
            EnsureLoaded();
            return Memory.Take(take).ToList();
        }
    }

    public static GuardIncident? LatestOpen()
    {
        lock (Gate)
        {
            EnsureLoaded();
            return Memory.FirstOrDefault(i => i.Open);
        }
    }

    public static bool Acknowledge(string id)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var hit = Memory.FirstOrDefault(i =>
                i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (hit is null) return false;
            hit.Open = false;
            hit.AcknowledgedAt = DateTimeOffset.Now;
            Rewrite();
            return true;
        }
    }

    public static int AcknowledgeAll()
    {
        lock (Gate)
        {
            EnsureLoaded();
            var n = 0;
            foreach (var i in Memory.Where(x => x.Open))
            {
                i.Open = false;
                i.AcknowledgedAt = DateTimeOffset.Now;
                n++;
            }
            if (n > 0) Rewrite();
            return n;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadLines(FilePath).TakeLast(80).Reverse())
            {
                var e = JsonSerializer.Deserialize<GuardIncident>(line, Json);
                if (e is not null) Memory.Add(e);
            }
        }
        catch { /* ignore */ }
    }

    private static void Rewrite()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            var lines = Memory.AsEnumerable().Reverse()
                .Select(i => JsonSerializer.Serialize(i, Json));
            File.WriteAllLines(FilePath, lines);
        }
        catch { /* ignore */ }
    }
}
