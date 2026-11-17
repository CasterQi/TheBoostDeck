using TheBoostDeck.Core;
using Xunit;

namespace TheBoostDeck.Tests;

public sealed class FixedSizeBufferTests
{
    [Fact]
    public void BufferKeepsNewestItemsInChronologicalOrder()
    {
        var buffer = new FixedSizeBuffer<int>(3);
        foreach (var value in new[] { 1, 2, 3, 4, 5 })
            buffer.Add(value);

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 3, 4, 5 }, buffer.Snapshot());
    }

    [Fact]
    public void ClearResetsBuffer()
    {
        var buffer = new FixedSizeBuffer<string>(2);
        buffer.Add("one");
        buffer.Clear();
        buffer.Add("two");

        Assert.Equal(1, buffer.Count);
        Assert.Equal(new[] { "two" }, buffer.Snapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidCapacityThrows(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedSizeBuffer<int>(capacity));
    }
}
