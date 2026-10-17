using Pomodim.Core.Brightness;

namespace Pomodim.Cli;

/// <summary>
/// The backends this build ships with, in selection order. Real OS backends (WinDdc,
/// MacIOKit, LinuxSysfs) join this list in later slices; the mock is always present so
/// CI/VM runs have something to drive.
/// </summary>
public static class BackendRegistry
{
    public static IReadOnlyList<IBrightnessBackend> CreateDefault() =>
    [
        new MockBrightnessBackend(),
    ];
}