using System.IO;
using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public sealed class PersistentStats
{
    public int BlockedStarts { get; set; }
    public int LaunchAttempts { get; set; }
    public int SuspendEvents { get; set; }
    /// <summary>12 hour buckets of activity counts (oldest → newest).</summary>
    public int[] ActivitySpark { get; set; } = new int[12];
    /// <summary>UTC hour tick when ActivitySpark[11] was last written.</summary>
    public long SparkHourUnix { get; set; }
}

public static class StatsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FilePath =>
        Path.Combine(SettingsStore.DirectoryPath, "stats.json");

    public static PersistentStats Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new PersistentStats();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<PersistentStats>(json, JsonOptions) ?? new PersistentStats();
        }
        catch
        {
            return new PersistentStats();
        }
    }

    public static void Save(PersistentStats stats)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(stats, JsonOptions));
        }
        catch
        {
            /* ignore */
        }
    }
}
