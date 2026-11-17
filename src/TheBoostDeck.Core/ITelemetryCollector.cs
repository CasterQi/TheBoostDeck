namespace TheBoostDeck.Core;

public interface ITelemetryCollector
{
    Task<TelemetrySnapshot> CollectAsync(CancellationToken cancellationToken);
}
