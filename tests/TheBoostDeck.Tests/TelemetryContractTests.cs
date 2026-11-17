using System.Text.Json;
using System.Text.Json.Serialization;
using TheBoostDeck.Core;
using Xunit;

namespace TheBoostDeck.Tests;

public sealed class TelemetryContractTests
{
    [Fact]
    public void UnavailableMetricKeepsZeroDistinctFromMissingData()
    {
        var unavailable = MetricValue.Unavailable("°C", "sensor unavailable");
        var zero = MetricValue.Available(0, "°C", "test");

        Assert.Null(unavailable.Value);
        Assert.Equal(MetricStatus.Unavailable, unavailable.Status);
        Assert.Equal(0, zero.Value);
        Assert.Equal(MetricStatus.Available, zero.Status);
    }

    [Fact]
    public void SnapshotSerializesToCamelCaseWithReadableStatus()
    {
        var snapshot = new TelemetrySnapshot
        {
            MachineName = "contract-test",
            Cpu = new CpuMetrics
            {
                Usage = MetricValue.Available(42.5, "%", "test"),
                Temperature = MetricValue.Unavailable("°C", "missing")
            },
            SensorDriver = new SensorDriverInfo
            {
                Status = "ready",
                Ready = true,
                Version = "2.2.0.0"
            }
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });

        Assert.Contains("\"machineName\":\"contract-test\"", json);
        Assert.Contains("\"status\":\"available\"", json);
        Assert.Contains("\"temperature\"", json);
        Assert.Contains("\"reason\":\"missing\"", json);
        Assert.Contains("\"sensorDriver\"", json);
        Assert.Contains("\"version\":\"2.2.0.0\"", json);

        var roundTrip = JsonSerializer.Deserialize<TelemetrySnapshot>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });

        Assert.True(roundTrip!.SensorDriver.Ready);
        Assert.Equal("2.2.0.0", roundTrip.SensorDriver.Version);
    }
}
