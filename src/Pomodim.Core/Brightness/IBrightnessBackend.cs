namespace Pomodim.Core.Brightness;

/// <summary>
/// A way to read and set display brightness on one platform. Implementations must be
/// free of UI dependencies: the break engine and the CLI only ever see this interface.
/// </summary>
public interface IBrightnessBackend
{
    /// <summary>Stable name used on the command line, e.g. "mock".</summary>
    string Name { get; }

    /// <summary>Every display this backend can see, in a stable order.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>
    /// Sets the brightness of one display, clamped to its [Min, Max] range.
    /// Throws <see cref="KeyNotFoundException"/> for unknown ids and
    /// <see cref="NotSupportedException"/> for unsupported displays.
    /// </summary>
    void SetBrightness(string id, int value);

    /// <summary>Captures the current value of every display.</summary>
    BrightnessSnapshot Snapshot();

    /// <summary>Restores every display in the snapshot to its captured value.</summary>
    void Restore(BrightnessSnapshot snapshot);
}