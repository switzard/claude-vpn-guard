using Microsoft.Win32;

namespace ClaudeVpnGuard.Core;

public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeVpnGuard";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                       ?? Registry.CurrentUser.CreateSubKey(RunKey);

        if (enabled)
        {
            var exe = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "ClaudeVpnGuard.exe");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static IReadOnlyList<string> FindClaudeAutostartEntries()
    {
        var hits = new List<string>();
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        if (key is null) return hits;

        foreach (var name in key.GetValueNames())
        {
            var value = key.GetValue(name)?.ToString() ?? "";
            if (value.Contains("Claude", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("ClaudeVpnGuard", StringComparison.OrdinalIgnoreCase))
            {
                hits.Add($"{name} = {value}");
            }
        }

        return hits;
    }

    public static int DisableClaudeAutostartEntries()
    {
        var removed = 0;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return 0;

        foreach (var name in key.GetValueNames().ToArray())
        {
            var value = key.GetValue(name)?.ToString() ?? "";
            if (value.Contains("Claude", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("ClaudeVpnGuard", StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(name, throwOnMissingValue: false);
                removed++;
            }
        }

        return removed;
    }
}
