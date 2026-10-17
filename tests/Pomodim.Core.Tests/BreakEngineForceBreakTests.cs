using Pomodim.Core.Break;

namespace Pomodim.Core.Tests;

/// <summary>
/// ForceBreak is what the tray's "Break now" does, so it has to obey the two rules the
/// rest of the engine obeys: a break always restarts its own duration, and a locked
/// session is never dimmed.
/// </summary>
public class BreakEngineForceBreakTests
{
    private static BreakEngine Engine(FakeClock clock) =>
        new(Protocols.TwentyFive, dimDepth: 30, clock);

    [Fact]
    public void ForceBreak_enters_break_and_dims_for_the_full_break_duration()
    {
        var clock = new FakeClock();
        var engine = Engine(clock);

        engine.ForceBreak();

        Assert.Equal(SessionState.Break, engine.State);
        Assert.Equal(BreakPhase.Break, engine.Phase);
        Assert.True(engine.IsDimmed);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.PhaseRemaining);
    }

    [Fact]
    public void ForceBreak_while_already_breaking_restarts_the_break()
    {
        var clock = new FakeClock();
        var engine = Engine(clock);

        engine.ForceBreak();
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(1), engine.PhaseRemaining);

        engine.ForceBreak();

        Assert.Equal(TimeSpan.FromMinutes(5), engine.PhaseRemaining);
        Assert.Equal(SessionState.Break, engine.State);
    }

    [Fact]
    public void ForceBreak_does_nothing_while_locked()
    {
        var clock = new FakeClock();
        var engine = Engine(clock);
        engine.Lock();

        engine.ForceBreak();

        Assert.Equal(SessionState.Locked, engine.State);
        Assert.False(engine.IsDimmed);
    }

    [Fact]
    public void ForceBreak_from_paused_starts_a_running_break()
    {
        var clock = new FakeClock();
        var engine = Engine(clock);
        engine.Pause();

        engine.ForceBreak();

        Assert.Equal(SessionState.Break, engine.State);
        Assert.True(engine.IsDimmed);
    }
}
