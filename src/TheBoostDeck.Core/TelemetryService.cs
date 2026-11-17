namespace TheBoostDeck.Core;

public sealed class TelemetryService : IAsyncDisposable
{
    private readonly ITelemetryCollector _collector;
    private readonly TimeSpan _interval;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
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

    public void SetPaused(bool paused) => _paused = paused;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
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
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cancellation;
        Task? runningTask;
        lock (_sync)
        {
            cancellation = _cancellation;
            runningTask = _runningTask;
            _cancellation = null;
            _runningTask = null;
        }

        if (cancellation is null)
            return;

        cancellation.Cancel();
        if (runningTask is not null)
            await runningTask.ConfigureAwait(false);
        cancellation.Dispose();
    }
}
