using Pomodim.Core.Break;

namespace Pomodim.Core.Tests;

/// <summary>A manually advanced clock for break-engine tests.</summary>
public sealed class FakeClock : IClock
{
    private DateTimeOffset _now;

    public FakeClock()
        : this(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero))
    {
    }

    public FakeClock(DateTimeOffset start) => _now = start;

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset value) => _now = value;
}