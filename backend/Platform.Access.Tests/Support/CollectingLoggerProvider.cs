using Microsoft.Extensions.Logging;

namespace Platform.Access.Tests.Support;

internal sealed record ProvidedLogEntry(string Category, LogLevel Level, EventId EventId, string Message);

internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly List<ProvidedLogEntry> _entries = [];

    public IReadOnlyList<ProvidedLogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CategoryLogger(string category, List<ProvidedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add(new ProvidedLogEntry(category, logLevel, eventId, formatter(state, exception)));
            }
        }
    }
}

internal sealed class ThrowingLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ThrowingLogger<object>();

    public void Dispose()
    {
    }
}
