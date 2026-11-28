using Microsoft.Extensions.Logging;

namespace Team21.Acc.Tests.Support;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordedLog> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> fields)
            throw new InvalidOperationException("Expected structured audit fields.");

        Entries.Add(new RecordedLog(
            logLevel,
            fields.ToDictionary(field => field.Key, field => field.Value),
            formatter(state, exception),
            exception));
    }
}

internal sealed record RecordedLog(
    LogLevel Level,
    IReadOnlyDictionary<string, object?> Fields,
    string Message,
    Exception? Exception);
