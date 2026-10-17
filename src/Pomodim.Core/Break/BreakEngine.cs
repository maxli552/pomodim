namespace Pomodim.Core.Break;

/// <summary>
/// The Pomodoro state machine. Pure logic: it reads time only through the injected
/// <see cref="IClock"/> and never touches the OS, a timer thread, or a brightness
/// backend. The host (tray app or CLI) calls <see cref="Tick"/> periodically and
/// reacts to <see cref="IsDimmed"/>.
///
/// Semantics:
/// - Work and Break phases run back to back; <see cref="Tick"/> rolls the phase over
///   when its deadline has passed.
/// - Pause freezes the phase; Resume continues it with the remaining time.
/// - Lock freezes the phase and suppresses dimming (a colour-critical session,
///   "don't dim while I'm proofing"). A locked session is never in the dimmed state;
///   Unlock resumes the phase with its remaining time.
/// </summary>
public sealed class BreakEngine
{
    private SessionState _state = SessionState.Work;
    private BreakPhase _phase = BreakPhase.Work;
    private DateTimeOffset _deadline;
    private TimeSpan _heldRemaining;

    public BreakEngine(BreakProtocol protocol, int dimDepth, IClock clock)
    {
        Protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (dimDepth is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(dimDepth), dimDepth, "Dim depth must be a percentage between 0 and 100.");
        DimDepth = dimDepth;
        _deadline = Clock.UtcNow + Protocol.WorkDuration;
    }

    public BreakProtocol Protocol { get; }

    public IClock Clock { get; }

    /// <summary>Brightness percentage to dim to during a break (0 = off, 100 = no dim).</summary>
    public int DimDepth { get; }

    public SessionState State => _state;

    /// <summary>The phase the timer is counting, even while paused or locked.</summary>
    public BreakPhase Phase => _phase;

    /// <summary>True only while a break is running and the session is not paused or locked.</summary>
    public bool IsDimmed => _state == SessionState.Break;

    /// <summary>Time left in the current phase; zero once the phase has elapsed but not yet ticked.</summary>
    public TimeSpan PhaseRemaining
    {
        get
        {
            if (_state is SessionState.Paused or SessionState.Locked)
                return _heldRemaining;
            var remaining = _deadline - Clock.UtcNow;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>
    /// Advances the state machine, rolling the phase over when its deadline has passed.
    /// A phase that has elapsed by more than one rollover needs one <see cref="Tick"/>
    /// per rollover; hosts tick at a frequency high relative to the phase lengths.
    /// No-op while paused or locked.
    /// </summary>
    public void Tick()
    {
        if (_state is SessionState.Paused or SessionState.Locked)
            return;
        var now = Clock.UtcNow;
        if (now < _deadline)
            return;
        if (_phase == BreakPhase.Work)
        {
            _phase = BreakPhase.Break;
            _state = SessionState.Break;
            _deadline = now + Protocol.BreakDuration;
        }
        else
        {
            _phase = BreakPhase.Work;
            _state = SessionState.Work;
            _deadline = now + Protocol.WorkDuration;
        }
    }

    /// <summary>Freezes the current phase. Idempotent while paused; a locked session must be unlocked first.</summary>
    public void Pause()
    {
        if (_state == SessionState.Locked)
            throw new InvalidOperationException("Cannot pause a locked session; unlock it first.");
        if (_state == SessionState.Paused)
            return;
        _heldRemaining = RemainingNow();
        _state = SessionState.Paused;
    }

    /// <summary>Continues the phase that was paused, with its remaining time.</summary>
    public void Resume()
    {
        if (_state != SessionState.Paused)
            throw new InvalidOperationException($"Resume is only valid from Paused (current state: {_state}).");
        _state = _phase == BreakPhase.Work ? SessionState.Work : SessionState.Break;
        _deadline = Clock.UtcNow + _heldRemaining;
    }

    /// <summary>
    /// Marks the session as colour-critical: the phase freezes and dimming is suppressed.
    /// Idempotent.
    /// </summary>
    public void Lock()
    {
        if (_state == SessionState.Locked)
            return;
        if (_state != SessionState.Paused)
            _heldRemaining = RemainingNow();
        _state = SessionState.Locked;
    }

    /// <summary>Ends the colour-critical session; the frozen phase resumes with its remaining time.</summary>
    public void Unlock()
    {
        if (_state != SessionState.Locked)
            throw new InvalidOperationException($"Unlock is only valid from Locked (current state: {_state}).");
        _state = _phase == BreakPhase.Work ? SessionState.Work : SessionState.Break;
        _deadline = Clock.UtcNow + _heldRemaining;
    }

    /// <summary>
    /// Starts a break immediately, for the person clicking "Break now". A locked session is
    /// left alone: locked means never dim, and that includes a break that was asked for.
    /// </summary>
    public void ForceBreak()
    {
        if (_state == SessionState.Locked)
            return;
        _phase = BreakPhase.Break;
        _state = SessionState.Break;
        _heldRemaining = TimeSpan.Zero;
        _deadline = Clock.UtcNow + Protocol.BreakDuration;
    }

    private TimeSpan RemainingNow()
    {
        var remaining = _deadline - Clock.UtcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}