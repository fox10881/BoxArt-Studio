using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;

namespace GameIconsStudio.Services;

/// <summary>
/// In-memory runtime log for the Debug Logs viewer. Thread-safe: entries
/// added from background tasks are marshalled to the UI thread's
/// DispatcherQueue before touching the ObservableCollection.
/// </summary>
public static class LogService
{
    private const int MaxEntries = 2000;
    private static DispatcherQueue? _queue;

    /// <summary>Live log lines — bind directly in the Debug dialog.</summary>
    public static ObservableCollection<string> Entries { get; } = new();

    /// <summary>Call once on the UI thread so background logs marshal correctly.</summary>
    public static void Init(DispatcherQueue queue) => _queue = queue;

    public static void Info(string message) => Add("INFO ", message);
    public static void Warn(string message) => Add("WARN ", message);
    public static void Error(string message) => Add("ERROR", message);

    public static void Clear()
    {
        if (_queue is null || _queue.HasThreadAccess) Entries.Clear();
        else _queue.TryEnqueue(static () => Entries.Clear());
    }

    private static void Add(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";
        void Append()
        {
            Entries.Add(line);
            if (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        }

        if (_queue is null || _queue.HasThreadAccess) Append();
        else _queue.TryEnqueue(Append);
    }
}
