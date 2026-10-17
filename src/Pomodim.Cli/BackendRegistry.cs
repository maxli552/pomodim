using Pomodim.Core.Brightness;

namespace Pomodim.Cli;

/// <summary>
/// The backends this build ships with, in selection order. Real OS backends (WinDdc,
/// MacIOKit, LinuxSysfs) join this list in later slices; the mock is always present so
/// CI/VM runs have something to drive. <c>mock-unsupported</c> is the same mock with
/// only unsupported displays, so the "nothing to dim" exit-3 path is reachable from
/// the command line (<c>--diag --backend mock-unsupported</c>).
/// </summary>
public static class BackendRegistry
{
    public static IReadOnlyList<IBrightnessBackend> CreateDefault() =>
    [
        new MockBrightnessBackend(),
        new MockBrightnessBackend(MockBrightnessBackend.UnsupportedOnlyDisplays(), name: "mock-unsupported"),
    ];
}