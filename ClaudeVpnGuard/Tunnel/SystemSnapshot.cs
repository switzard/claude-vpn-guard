using System.Net.NetworkInformation;
using System.Diagnostics;

namespace ClaudeVpnGuard.Tunnel;

public sealed record ProcessInfo(string Name, int Id, string? Path);

public sealed record AdapterInfo(string Name, string Description, OperationalStatus Status);

public sealed class SystemSnapshot
{
    public required IReadOnlyList<ProcessInfo> Processes { get; init; }
    public required IReadOnlyList<AdapterInfo> Adapters { get; init; }

    public bool HasProcess(params string[] names) =>
        Processes.Any(p => names.Any(n =>
            p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));

    public bool HasProcessContains(params string[] fragments) =>
        Processes.Any(p => fragments.Any(f =>
            p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));

    public AdapterInfo? FindAdapter(Func<AdapterInfo, bool> predicate) =>
        Adapters.FirstOrDefault(predicate);

    public static SystemSnapshot Capture()
    {
        var processes = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* access denied */ }
                processes.Add(new ProcessInfo(p.ProcessName, p.Id, path));
            }
            catch
            {
                // process exited
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Select(n => new AdapterInfo(n.Name, n.Description, n.OperationalStatus))
            .ToList();

        return new SystemSnapshot
        {
            Processes = processes,
            Adapters = adapters
        };
    }
}
