using Pomodim.Core.Break;
using Xunit;

namespace Pomodim.Core.Tests;

public class BreakEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Presets_have_the_planned_durations()
    {
        Assert.Equal("20/20/20", Protocols.TwentyTwentyTwenty.Name);
        Assert.Equal(TimeSpan.FromMinutes(22), Protocols.TwentyTwentyTwenty.WorkDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), Protocols.TwentyTwentyTwenty.BreakDuration);

        Assert.Equal("20/5", Protocols.TwentyFive.Name);
        Assert.Equal(TimeSpan.FromMinutes(20), Protocols.TwentyFive.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), Protocols.TwentyFive.BreakDuration);

        Assert.Equal("45/5", Protocols.FortyFiveFive.Name);
        Assert.Equal(TimeSpan.FromMinutes(45), Protocols.FortyFiveFive.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), Protocols.FortyFiveFive.BreakDuration);

        Assert.Equal("custom", Protocols.Custom(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)).Name);
    }

    [Fact]
    public void Engine_starts_in_work_not_dimmed()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        Assert.Equal(SessionState.Work, engine.State);
        Assert.Equal(BreakPhase.Work, engine.Phase);
        Assert.False(engine.IsDimmed);
        Assert.Equal(40, engine.DimDepth);
    }

    [Fact]
    public void Work_rolls_over_to_break_and_back()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(19));
        engine.Tick();
        Assert.Equal(SessionState.Work, engine.State);
        Assert.False(engine.IsDimmed);

        clock.Advance(TimeSpan.FromMinutes(1)); // 20 minutes of work done
        engine.Tick();
        Assert.Equal(SessionState.Break, engine.State);
        Assert.Equal(BreakPhase.Break, engine.Phase);
        Assert.True(engine.IsDimmed);

        clock.Advance(TimeSpan.FromMinutes(5)); // 5-minute break done
        engine.Tick();
        Assert.Equal(SessionState.Work, engine.State);
        Assert.False(engine.IsDimmed);
    }

    [Fact]
    public void Twenty_twenty_twenty_uses_22_minute_work_and_20_second_break()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyTwentyTwenty, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(22));
        engine.Tick();
        Assert.Equal(SessionState.Break, engine.State);

        clock.Advance(TimeSpan.FromSeconds(20));
        engine.Tick();
        Assert.Equal(SessionState.Work, engine.State);
    }

    [Fact]
    public void Custom_protocol_drives_the_schedule()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.Custom(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10)), 30, clock);

        clock.Advance(TimeSpan.FromMinutes(30));
        engine.Tick();
        Assert.Equal(SessionState.Break, engine.State);

        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Tick();
        Assert.Equal(SessionState.Work, engine.State);
    }

    [Fact]
    public void Pause_freezes_the_phase_and_resume_continues_it()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Pause();
        Assert.Equal(SessionState.Paused, engine.State);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.PhaseRemaining);

        clock.Advance(TimeSpan.FromMinutes(30)); // far past the original deadline
        engine.Tick();
        Assert.Equal(SessionState.Paused, engine.State);
        Assert.False(engine.IsDimmed);

        engine.Resume();
        Assert.Equal(SessionState.Work, engine.State);

        clock.Advance(TimeSpan.FromMinutes(9));
        engine.Tick();
        Assert.Equal(SessionState.Work, engine.State);

        clock.Advance(TimeSpan.FromMinutes(1)); // 10 minutes after resume
        engine.Tick();
        Assert.Equal(SessionState.Break, engine.State);
        Assert.True(engine.IsDimmed);
    }

    [Fact]
    public void Pause_during_break_freezes_the_break_and_suppresses_dimming()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(20));
        engine.Tick();
        Assert.Equal(SessionState.Break, engine.State);
        Assert.True(engine.IsDimmed);

        clock.Advance(TimeSpan.FromMinutes(2));
        engine.Pause();
        Assert.Equal(SessionState.Paused, engine.State);
        Assert.False(engine.IsDimmed);

        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Tick();
        Assert.Equal(SessionState.Paused, engine.State);

        engine.Resume();
        Assert.Equal(SessionState.Break, engine.State);
        Assert.True(engine.IsDimmed);

        clock.Advance(TimeSpan.FromMinutes(3)); // 2 + 3 = 5-minute break done
        engine.Tick();
        Assert.Equal(SessionState.Work, engine.State);
    }

    [Fact]
    public void Locked_session_never_enters_the_dimmed_state()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Lock();
        Assert.Equal(SessionState.Locked, engine.State);
        Assert.False(engine.IsDimmed);

        // The work phase would have ended while locked; the timer is frozen.
        clock.Advance(TimeSpan.FromMinutes(30));
        engine.Tick();
        Assert.Equal(SessionState.Locked, engine.State);
        Assert.False(engine.IsDimmed);

        engine.Unlock();
        Assert.Equal(SessionState.Work, engine.State);
        Assert.False(engine.IsDimmed);

        // The remaining 10 minutes of work still run after unlock.
        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Tick();
        Assert.Equal(SessionState.Break, engine.State);
        Assert.True(engine.IsDimmed);
    }

    [Fact]
    public void Lock_during_break_suppresses_dimming_until_unlock()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(20));
        engine.Tick();
        Assert.True(engine.IsDimmed);

        engine.Lock();
        Assert.Equal(SessionState.Locked, engine.State);
        Assert.False(engine.IsDimmed);

        clock.Advance(TimeSpan.FromMinutes(10));
        engine.Tick();
        Assert.Equal(SessionState.Locked, engine.State);
        Assert.False(engine.IsDimmed);

        engine.Unlock();
        Assert.Equal(SessionState.Break, engine.State);
        Assert.True(engine.IsDimmed);
    }

    [Fact]
    public void Lock_is_idempotent_and_pause_while_locked_throws()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        engine.Lock();
        engine.Lock();
        Assert.Equal(SessionState.Locked, engine.State);

        Assert.Throws<InvalidOperationException>(() => engine.Pause());
    }

    [Fact]
    public void Resume_and_unlock_out_of_state_throw()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        Assert.Throws<InvalidOperationException>(() => engine.Resume());
        Assert.Throws<InvalidOperationException>(() => engine.Unlock());
    }

    [Fact]
    public void Dim_depth_out_of_range_is_rejected()
    {
        var clock = new FakeClock(Start);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakEngine(Protocols.TwentyFive, 101, clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakEngine(Protocols.TwentyFive, -1, clock));
    }

    [Fact]
    public void Phase_remaining_counts_down()
    {
        var clock = new FakeClock(Start);
        var engine = new BreakEngine(Protocols.TwentyFive, 40, clock);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(15), engine.PhaseRemaining);
    }
}