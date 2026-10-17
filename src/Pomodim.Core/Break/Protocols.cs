namespace Pomodim.Core.Break;

/// <summary>
/// The built-in protocols. 20/20/20 follows the classic rule as specified in PLAN.md:
/// 22 minutes of work, then a 20-second break looking 20 feet away.
/// </summary>
public static class Protocols
{
    public static readonly BreakProtocol TwentyTwentyTwenty =
        new("20/20/20", TimeSpan.FromMinutes(22), TimeSpan.FromSeconds(20));

    public static readonly BreakProtocol TwentyFive =
        new("20/5", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5));

    public static readonly BreakProtocol FortyFiveFive =
        new("45/5", TimeSpan.FromMinutes(45), TimeSpan.FromMinutes(5));

    /// <summary>A user-defined protocol with any positive work and break lengths.</summary>
    public static BreakProtocol Custom(TimeSpan workDuration, TimeSpan breakDuration) =>
        new("custom", workDuration, breakDuration);
}