using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string DirectoryPath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeVpnGuard");

    public static string FilePath => System.IO.Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath))
                return new AppSettings();

            var json = System.IO.File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            foreach (var (key, value) in AppSettings.CreateDefaultProviders())
            {
                if (!settings.EnabledProviders.ContainsKey(key))
                    settings.EnabledProviders[key] = value;
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        System.IO.File.WriteAllText(FilePath, json);
    }
}
