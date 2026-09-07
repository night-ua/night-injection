using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.Logging;

public sealed class AppLogProvider : ILoggerProvider, IAppLogStore
{
    private const int MaximumVisibleEntries = 5_000;
    private readonly IAppPathService _paths;
    private readonly ConcurrentQueue<AppLogEntry> _entries = new();
    private readonly Channel<AppLogEntry> _fileQueue = Channel.CreateBounded<AppLogEntry>(
        new BoundedChannelOptions(4_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writer;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public AppLogProvider(IAppPathService paths)
    {
        _paths = paths;
        _paths.EnsureApplicationDirectories();
        _writer = Task.Run(WriteLoopAsync);
    }

    public event EventHandler<AppLogEntry>? EntryAdded;

    public ILogger CreateLogger(string categoryName) => new AppLogger(this, categoryName);

    public IReadOnlyList<AppLogEntry> Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
        _fileQueue.Writer.TryComplete();
        _shutdown.CancelAfter(TimeSpan.FromSeconds(2));
        _ = _writer.ContinueWith(
            _ => _shutdown.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Publish(AppLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaximumVisibleEntries && _entries.TryDequeue(out _))
        {
        }

        _fileQueue.Writer.TryWrite(entry);
        EntryAdded?.Invoke(this, entry);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var entry in _fileQueue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
        {
            var path = Path.Combine(_paths.LogsRoot, $"night-injection_{entry.Timestamp:yyyyMMdd}.log");
            var line =
                $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{entry.Level}] {entry.Category}: {entry.Message}";
            if (!string.IsNullOrWhiteSpace(entry.ExceptionDetail))
            {
                line += Environment.NewLine + entry.ExceptionDetail;
            }

            try
            {
                await File.AppendAllTextAsync(
                    path,
                    line + Environment.NewLine,
                    _shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                                 or UnauthorizedAccessException
                                                 or OperationCanceledException)
            {
            }
        }
    }

    private sealed class AppLogger(AppLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= provider.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            provider.Publish(new AppLogEntry(
                DateTimeOffset.Now,
                logLevel,
                category,
                message,
                exception?.ToString()));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose()
        {
        }
    }
}
