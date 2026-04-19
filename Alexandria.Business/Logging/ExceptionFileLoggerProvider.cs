using System.Text;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Logging;

/// <summary>
/// Writes every logged <see cref="LogLevel.Error"/> that carries an exception to an append-mode
/// file. The writer is opened once and shared across every <see cref="ExceptionFileLogger"/>
/// this provider creates; <see cref="Dispose"/> flushes and closes it on host shutdown. If the
/// log file is deleted externally while the host is running, the next write reopens it so log
/// rotation or manual cleanup never silently discards errors.
/// </summary>
public sealed class ExceptionFileLoggerProvider : ILoggerProvider
{
    private readonly string logFilePath;
    private readonly Lock gate = new();
    private StreamWriter writer;

    public ExceptionFileLoggerProvider(string logFilePath)
    {
        this.logFilePath = logFilePath;
        writer = OpenWriter();
    }

    public ILogger CreateLogger(string categoryName) => new ExceptionFileLogger(this);

    internal void Write(string timestamp, string message, Exception exception)
    {
        lock (gate)
        {
            if (!File.Exists(logFilePath))
            {
                writer.Dispose();
                writer = OpenWriter();
            }

            writer.WriteLine($"{timestamp} => {message}");
            writer.WriteLine(exception.ToString());
            writer.WriteLine();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer.Flush();
            writer.Dispose();
        }
    }

    private StreamWriter OpenWriter()
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var stream = File.Open(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }
}
