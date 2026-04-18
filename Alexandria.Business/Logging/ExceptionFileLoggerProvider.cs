using System.Text;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Logging;

/// <summary>
/// Writes every logged <see cref="LogLevel.Error"/> that carries an exception to an append-mode
/// file. The writer is opened once and shared across every <see cref="ExceptionFileLogger"/>
/// this provider creates; <see cref="Dispose"/> flushes and closes it on host shutdown.
/// </summary>
public sealed class ExceptionFileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter writer;
    private readonly Lock gate = new();

    public ExceptionFileLoggerProvider(string logFilePath)
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var stream = File.Open(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName) => new ExceptionFileLogger(writer, gate);

    public void Dispose()
    {
        lock (gate)
        {
            writer.Flush();
            writer.Dispose();
        }
    }
}
