using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ADOFAI.ModManager.Services.Ui;

/// <summary>极简文件日志（写入 %LOCALAPPDATA%\ADOFAI.ModManager\logs\app-yyyyMMdd.log）。</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 4096);
    private readonly Thread _writerThread;
    private readonly string _logDirectory;

    public FileLoggerProvider()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ADOFAI.ModManager", "logs");
        Directory.CreateDirectory(_logDirectory);

        _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "modui-file-logger" };
        _writerThread.Start();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _queue));

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // 日志写失败不影响应用
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }

        foreach (var logger in _loggers.Values)
        {
            logger.Dispose();
        }

        _loggers.Clear();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly BlockingCollection<string> _queue;
        private bool _disposed;

        public FileLogger(string category, BlockingCollection<string> queue)
        {
            _category = category;
            _queue = queue;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && !_disposed;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{logLevel switch { LogLevel.Error => "ERR", LogLevel.Warning => "WRN", _ => "INF" }}] {_category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            try
            {
                _queue.TryAdd(line + Environment.NewLine);
            }
            catch (InvalidOperationException)
            {
                // 队列已完成（退出中）
            }
        }

        public void Dispose() => _disposed = true;
    }
}
