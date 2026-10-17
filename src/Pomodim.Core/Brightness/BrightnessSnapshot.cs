namespace Pomodim.Core.Brightness;

/// <summary>
/// The brightness of every display at one moment, keyed by display id.
/// Taken by <see cref="IBrightnessBackend.Snapshot"/> and returned by
/// <see cref="IBrightnessBackend.Restore"/>.
/// </summary>
public sealed record BrightnessSnapshot(
    string BackendName,
    IReadOnlyDictionary<string, int> Values);