using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Alexandria.Tests.Data;

internal sealed record CapturedLogEntry(LogLevel Level, Exception? Exception, string Message)
{
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentBag<CapturedLogEntry> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new CapturedLogEntry(logLevel, exception, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
