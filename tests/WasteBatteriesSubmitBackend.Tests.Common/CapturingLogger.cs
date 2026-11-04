using Microsoft.Extensions.Logging;

namespace WasteBatteriesSubmitBackend.Tests.Common;

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));

    public sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
}
