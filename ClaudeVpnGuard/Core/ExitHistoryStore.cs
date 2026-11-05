using System.IO;
using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public sealed class ExitHistoryEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string? Ip { get; set; }
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? Source { get; set; }
    public bool Ok { get; set; }
    public string? Note { get; set; }
}

public static class ExitHistoryStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly object Gate = new();
    private static readonly List<ExitHistoryEntry> Memory = [];

    public static string FilePath =>
        Path.Combine(SettingsStore.DirectoryPath, "exit-history.jsonl");

    public static void Append(ExitHistoryEntry entry)
    {
        lock (Gate)
        {
            var last = Memory.FirstOrDefault();
            if (last is not null &&
                string.Equals(last.Ip, entry.Ip, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(last.CountryCode, entry.CountryCode, StringComparison.OrdinalIgnoreCase) &&
                last.Ok == entry.Ok)
                return;

            Memory.Insert(0, entry);
            while (Memory.Count > 200) Memory.RemoveAt(Memory.Count - 1);
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
            }
            catch { /* ignore */ }
        }
    }

    public static IReadOnlyList<ExitHistoryEntry> Recent(int take = 40)
    {
        lock (Gate)
        {
            if (Memory.Count == 0) LoadTail();
            return Memory.Take(take).ToList();
        }
    }

    private static void LoadTail()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadLines(FilePath).TakeLast(80).Reverse())
            {
                var e = JsonSerializer.Deserialize<ExitHistoryEntry>(line, Json);
                if (e is not null) Memory.Add(e);
            }
        }
        catch { /* ignore */ }
    }
}
