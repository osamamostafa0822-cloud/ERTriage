using Microsoft.Extensions.Logging;

namespace ERTriage.Application.Tests;

internal sealed record LogEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State, Exception? Exception);

/// <summary>Hand-written ILogger&lt;T&gt; that records entries (message plus structured state) for assertions.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = (state as IEnumerable<KeyValuePair<string, object?>>)?.ToDictionary(p => p.Key, p => p.Value)
            ?? [];

        Entries.Add(new LogEntry(logLevel, formatter(state, exception), values, exception));
    }

    /// <summary>Everything an entry exposes (rendered message plus every structured value), for "must not contain" checks.</summary>
    public string AllText() =>
        string.Join("\n", Entries.Select(e => e.Message + "|" + string.Join(",", e.State.Select(s => $"{s.Key}={s.Value}"))));
}
