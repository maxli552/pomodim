namespace Pomodim.Core.Break;

/// <summary>Where the session is right now.</summary>
public enum SessionState
{
    Work,
    Break,
    Paused,
    Locked,
}

/// <summary>Which phase the timer is counting, even while paused or locked.</summary>
public enum BreakPhase
{
    Work,
    Break,
}