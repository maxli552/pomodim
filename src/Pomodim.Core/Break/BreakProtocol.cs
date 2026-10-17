namespace Pomodim.Core.Break;

/// <summary>
/// A break protocol: how long a work phase lasts and how long the break lasts.
/// <see cref="Name"/> is what the user sees ("20/5", "custom", ...).
/// </summary>
public sealed record BreakProtocol
{
    public string Name { get; }
    public TimeSpan WorkDuration { get; }
    public TimeSpan BreakDuration { get; }

    public BreakProtocol(string name, TimeSpan workDuration, TimeSpan breakDuration)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Protocol name must not be empty.", nameof(name));
        if (workDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(workDuration), workDuration, "Work duration must be positive.");
        if (breakDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(breakDuration), breakDuration, "Break duration must be positive.");
        Name = name;
        WorkDuration = workDuration;
        BreakDuration = breakDuration;
    }
}