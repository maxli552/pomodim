using Pomodim.Core.Break;
using Xunit;

namespace Pomodim.Core.Tests;

public class BreakProtocolTests
{
    [Fact]
    public void Zero_or_negative_durations_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BreakProtocol("x", TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BreakProtocol("x", TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(
            () => new BreakProtocol("  ", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5)));
    }
}