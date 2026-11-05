using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace ClaudeVpnGuard.Core;

public sealed class ClaudeProcessInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? Path { get; init; }
    public bool Suspended { get; set; }
}

public sealed class ProcessGuard : IDisposable
{
    private readonly EventJournal _journal;
    private readonly HashSet<int> _suspended = [];
    private readonly Dictionary<string, string> _lastPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private ManagementEventWatcher? _watcher;
    private bool _allowRunning = false;
    private bool _killInsteadOfSuspend;
    private bool _hadClaudeBeforeDrop;

    public event Action<int, string>? Blocked;
    public event Action<int>? DetectedStart;
    public event Action? Suspended;
    public IReadOnlyDictionary<string, string> LastKnownPaths
    {
        get { lock (_gate) return new Dictionary<string, string>(_lastPaths); }
    }
    public bool HadClaudeBeforeDrop
    {
        get { lock (_gate) return _hadClaudeBeforeDrop; }
        set { lock (_gate) _hadClaudeBeforeDrop = value; }
    }

    public ProcessGuard(EventJournal journal)
    {
        _journal = journal;
    }

    public void StartWatching()
    {
        try
        {
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'claude.exe' OR ProcessName = 'Claude.exe'");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnProcessStarted;
            _watcher.Start();
            _journal.Add("watch", "WMI process start watch armed");
        }
        catch (Exception ex)
        {
            _journal.Add("watch", $"WMI unavailable ({ex.Message}); polling only");
        }
    }

    public void SetAllowed(bool allowed) => _allowRunning = allowed;

    public void SetKillMode(bool killInsteadOfSuspend) => _killInsteadOfSuspend = killInsteadOfSuspend;

    public IReadOnlyList<ClaudeProcessInfo> ListClaudeProcesses()
    {
        var list = new List<ClaudeProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!IsClaudeProcess(p))
                    continue;

                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* ignore */ }

                var kind = Classify(path, p.ProcessName);
                lock (_gate)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        _lastPaths[kind] = path!;
                    list.Add(new ClaudeProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Kind = kind,
                        Path = path,
                        Suspended = _suspended.Contains(p.Id)
                    });
                }
            }
            catch
            {
                // exited
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        return list;
    }

    public void Enforce(bool allowRunning, bool killInsteadOfSuspend)
    {
        _allowRunning = allowRunning;
        _killInsteadOfSuspend = killInsteadOfSuspend;
        var processes = ListClaudeProcesses();

        if (allowRunning)
        {
            // Always force-resume every Claude process. Tracking alone is not enough:
            // another Guard instance (or a prior run) may have suspended them without
            // this process knowing, leaving Claude frozen forever.
            foreach (var info in processes)
                Resume(info.Id, force: true);
            return;
        }

        if (processes.Count > 0)
        {
            lock (_gate) { _hadClaudeBeforeDrop = true; }
        }

        foreach (var info in processes)
        {
            if (killInsteadOfSuspend)
                Kill(info.Id, info.Kind);
            else
                Suspend(info.Id, info.Kind);
        }
    }

    public int RelaunchRemembered()
    {
        Dictionary<string, string> paths;
        lock (_gate)
        {
            if (!_hadClaudeBeforeDrop || _lastPaths.Count == 0)
                return 0;
            paths = new Dictionary<string, string>(_lastPaths);
            _hadClaudeBeforeDrop = false;
        }

        var launched = 0;
        foreach (var (kind, path) in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                if (ListClaudeProcesses().Any(p =>
                        string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase) && !p.Suspended))
                    continue;

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                _journal.Add("launch", $"Автозапуск после VPN: {kind}");
                launched++;
            }
            catch (Exception ex)
            {
                _journal.Add("error", $"Автозапуск {kind}: {ex.Message}");
            }
        }
        return launched;
    }

    public void SuspendAll(string reason)
    {
        foreach (var info in ListClaudeProcesses())
            Suspend(info.Id, $"{info.Kind}: {reason}");
    }

    public void ResumeAll(string reason)
    {
        foreach (var info in ListClaudeProcesses())
            Resume(info.Id, reason, force: true);
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            var name = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "claude.exe";
            _journal.Add("launch", $"Detected start: {name} PID {pid}");
            DetectedStart?.Invoke(pid);

            if (_allowRunning)
                return;

            Thread.Sleep(120);
            var reason = "VPN/локация не готовы";
            if (_killInsteadOfSuspend)
                Kill(pid, reason);
            else
                Suspend(pid, reason);
            Blocked?.Invoke(pid, "VPN не активен или локация неверна");
        }
        catch (Exception ex)
        {
            _journal.Add("error", $"Ошибка watch: {ex.Message}");
        }
    }

    private void Suspend(int pid, string reason)
    {
        lock (_gate)
        {
            if (_suspended.Contains(pid))
                return;

            var handle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_SUSPEND_RESUME | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                false, pid);
            if (handle == IntPtr.Zero)
            {
                // Soft-fail is a killswitch hole — escalate to Kill when possible.
                _journal.Add("error", $"OpenProcess failed for PID {pid} — escalating to terminate");
                Kill(pid, $"{reason} (OpenProcess denied)");
                return;
            }

            try
            {
                var status = NativeMethods.NtSuspendProcess(handle);
                if (status == 0)
                {
                    _suspended.Add(pid);
                    _journal.Add("suspend", $"Suspended PID {pid} — {reason}");
                    Suspended?.Invoke();
                }
                else
                {
                    _journal.Add("error", $"NtSuspendProcess status 0x{status:X} for PID {pid} — escalating");
                    Kill(pid, $"{reason} (suspend 0x{status:X})");
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    private void Resume(int pid, string? reason = null, bool force = false)
    {
        lock (_gate)
        {
            var tracked = _suspended.Contains(pid);
            if (!force && !tracked)
                return;

            var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, pid);
            if (handle == IntPtr.Zero)
            {
                _suspended.Remove(pid);
                return;
            }

            try
            {
                // NtSuspendProcess nests: each suspend needs a matching resume.
                // Duplicate Guard instances can stack several counts on one PID.
                for (var i = 0; i < 4; i++)
                    NativeMethods.NtResumeProcess(handle);

                // Journal only when we knew about the suspend — force-unwinding every
                // poll would otherwise spam events.jsonl.
                var wasTracked = _suspended.Remove(pid);
                if (wasTracked)
                    _journal.Add("resume", $"Resumed PID {pid}" + (reason is null ? "" : $" — {reason}"));
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    private void Kill(int pid, string reason)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            lock (_gate) { _suspended.Remove(pid); }
            _journal.Add("kill", $"Terminated PID {pid} — {reason}");
        }
        catch (Exception ex)
        {
            _journal.Add("error", $"Kill PID {pid} failed: {ex.Message}");
        }
    }

    public static bool IsClaudeProcess(Process p)
    {
        if (!p.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase) &&
            !p.ProcessName.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            return false;

        string? path = null;
        try { path = p.MainModule?.FileName; } catch { /* ignore */ }

        // Exclude this guard if somehow named similarly
        if (path is not null && path.Contains("ClaudeVpnGuard", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public static string Classify(string? path, string processName)
    {
        if (path is null)
            return "Claude";

        if (path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Claude_", StringComparison.OrdinalIgnoreCase))
            return "Claude Desktop";

        if (path.Contains("claude-code", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("@anthropic-ai", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("npm", StringComparison.OrdinalIgnoreCase))
            return "Claude Code";

        return processName;
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.Stop();
            _watcher.EventArrived -= OnProcessStarted;
            _watcher.Dispose();
        }
        catch { /* ignore */ }
        _watcher = null;
    }
}
