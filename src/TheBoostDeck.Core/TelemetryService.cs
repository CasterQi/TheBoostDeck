using System.Diagnostics;

namespace TheBoostDeck.Core;

public sealed class TelemetryService : IAsyncDisposable
{
    private readonly ITelemetryCollector _collector;
    private readonly object _sync = new();
    private TimeSpan _interval;
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _waitCancellation;
    private Task? _runningTask;
    private volatile bool _paused;

    public TelemetryService(ITelemetryCollector collector, TimeSpan? interval = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _interval = interval ?? TimeSpan.FromSeconds(1);
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
    }

    public event EventHandler<TelemetrySnapshot>? SnapshotAvailable;
    public event EventHandler<Exception>? CollectionFailed;

    public void Start()
    {
        lock (_sync)
        {
            if (_runningTask is not null)
                throw new InvalidOperationException("The telemetry service has already been started.");

            _cancellation = new CancellationTokenSource();
            _runningTask = RunAsync(_cancellation.Token);
        }
    }

    public void SetInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        lock (_sync)
        {
            if (_interval == interval)
                return;
            _interval = interval;
            _waitCancellation?.Cancel();
        }
    }

    public void SetPaused(bool paused) => _paused = paused;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Restart();
                if (!_paused)
                {
                    try
                    {
                        var snapshot = await _collector.CollectAsync(cancellationToken).ConfigureAwait(false);
                        SnapshotAvailable?.Invoke(this, snapshot);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        CollectionFailed?.Invoke(this, exception);
                    }
                }

                TimeSpan interval;
                lock (_sync)
                    interval = _interval;

                var delay = interval - stopwatch.Elapsed;
                if (delay <= TimeSpan.Zero)
                    delay = TimeSpan.FromMilliseconds(1);

                CancellationTokenSource waitCancellation;
                lock (_sync)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _waitCancellation = waitCancellation;
                }

                try
                {
                    await Task.Delay(delay, waitCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The interval changed while waiting. Loop immediately and use
                    // the new interval for the next sampling cycle.
                }
                finally
                {
                    lock (_sync)
                    {
                        if (ReferenceEquals(_waitCancellation, waitCancellation))
                            _waitCancellation = null;
                    }
                    waitCancellation.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        Task? runningTask;
        CancellationTokenSource? waitCancellation;
        lock (_sync)
        {
            cancellation = _cancellation;
            runningTask = _runningTask;
            waitCancellation = _waitCancellation;
            _cancellation = null;
            _runningTask = null;
            _waitCancellation = null;
        }

        if (cancellation is null)
            return;

        waitCancellation?.Cancel();
        cancellation.Cancel();
        if (runningTask is not null)
            await runningTask.ConfigureAwait(false);
        cancellation.Dispose();
    }
}
