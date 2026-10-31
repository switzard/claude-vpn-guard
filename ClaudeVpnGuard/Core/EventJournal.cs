using System.Collections.ObjectModel;
using System.Text.Json;

namespace ClaudeVpnGuard.Core;

public sealed class JournalEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Kind { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class EventJournal
{
    private readonly object _gate = new();
    private readonly string _logPath;
    private readonly ObservableCollection<JournalEntry> _entries = [];

    public EventJournal()
    {
        System.IO.Directory.CreateDirectory(SettingsStore.DirectoryPath);
        _logPath = System.IO.Path.Combine(SettingsStore.DirectoryPath, "events.jsonl");
        LoadTail();
    }

    public ObservableCollection<JournalEntry> Entries => _entries;

    public void Add(string kind, string message)
    {
        var entry = new JournalEntry { Kind = kind, Message = message };
        lock (_gate)
        {
            _entries.Insert(0, entry);
            while (_entries.Count > 300)
                _entries.RemoveAt(_entries.Count - 1);

            try
            {
                System.IO.File.AppendAllText(_logPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch
            {
                // disk full / locked — keep memory journal
            }
        }
    }

    private void LoadTail()
    {
        try
        {
            if (!System.IO.File.Exists(_logPath))
                return;

            var lines = System.IO.File.ReadLines(_logPath).TakeLast(80);
            foreach (var line in lines.Reverse())
            {
                var entry = JsonSerializer.Deserialize<JournalEntry>(line);
                if (entry is not null)
                    _entries.Add(entry);
            }
        }
        catch
        {
            // ignore corrupt log
        }
    }
}
