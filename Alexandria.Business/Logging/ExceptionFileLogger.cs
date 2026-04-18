using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Logging;

/// <summary>
/// Logger paired with <see cref="ExceptionFileLoggerProvider"/>. Only accepts
/// <see cref="LogLevel.Error"/> entries that carry a non-null exception; every
/// other call is a no-op so the file is Error-only by construction.
/// </summary>
internal sealed class ExceptionFileLogger(StreamWriter writer, Lock gate) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel != LogLevel.Error || exception is null)
            return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var message = formatter(state, null);

        lock (gate)
        {
            writer.WriteLine($"{timestamp} => {message}");
            writer.WriteLine(exception.ToString());
            writer.WriteLine();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
