namespace Pomodim.Core.Break;

/// <summary>
/// Time source for the break engine. The engine never touches the OS clock directly;
/// tests inject a fake.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}