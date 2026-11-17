using TheBoostDeck.Core;
using Xunit;

namespace TheBoostDeck.Tests;

public sealed class TelemetryServiceTests
{
    [Fact]
    public async Task ServicePublishesFirstSnapshotWithoutWaitingOneInterval()
    {
        var collector = new FakeCollector(async cancellationToken =>
        {
            await Task.Yield();
            return new TelemetrySnapshot { MachineName = "test-machine" };
        });
        var received = new TaskCompletionSource<TelemetrySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var service = new TelemetryService(collector, TimeSpan.FromSeconds(5));
        service.SnapshotAvailable += (_, snapshot) => received.TrySetResult(snapshot);
        service.Start();

        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("test-machine", snapshot.MachineName);
    }

    [Fact]
    public async Task SlowCollectionIsNeverOverlapped()
    {
        var active = 0;
        var maximumActive = 0;
        var collector = new FakeCollector(async cancellationToken =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedMax(ref maximumActive, current);
            try
            {
                await Task.Delay(80, cancellationToken);
                return new TelemetrySnapshot();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        await using (var service = new TelemetryService(collector, TimeSpan.FromMilliseconds(10)))
        {
            service.Start();
            await Task.Delay(260);
        }

        Assert.Equal(1, maximumActive);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task PausedServiceSkipsCollectionUntilResumed()
    {
        var collectionCount = 0;
        var received = new TaskCompletionSource<TelemetrySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = new FakeCollector(_ =>
        {
            Interlocked.Increment(ref collectionCount);
            return Task.FromResult(new TelemetrySnapshot());
        });

        await using var service = new TelemetryService(collector, TimeSpan.FromMilliseconds(20));
        service.SnapshotAvailable += (_, snapshot) => received.TrySetResult(snapshot);
        service.SetPaused(true);
        service.Start();
        await Task.Delay(90);
        Assert.Equal(0, collectionCount);

        service.SetPaused(false);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, collectionCount);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current)
                return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private sealed class FakeCollector : ITelemetryCollector
    {
        private readonly Func<CancellationToken, Task<TelemetrySnapshot>> _collect;

        public FakeCollector(Func<CancellationToken, Task<TelemetrySnapshot>> collect) => _collect = collect;

        public Task<TelemetrySnapshot> CollectAsync(CancellationToken cancellationToken) => _collect(cancellationToken);
    }
}
