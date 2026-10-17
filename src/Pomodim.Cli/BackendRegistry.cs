using Pomodim.Core.Brightness;
using Pomodim.Backends.Windows;

namespace Pomodim.Cli;

/// <summary>
/// The backends this build ships with, in selection order. The mock backends are
/// always present so CI/VM runs have something to drive; on Windows the real
/// Windows backend (DDC/CI + WMI) is registered and is the default when no
/// <c>--backend</c> is given. <c>mock-unsupported</c> is the same mock with only
/// unsupported displays, so the "nothing to dim" exit-3 path is reachable from
/// the command line (<c>--diag --backend mock-unsupported</c>).
/// </summary>
public static class BackendRegistry
{
    public static IReadOnlyList<IBrightnessBackend> CreateDefault()
    {
        var backends = new List<IBrightnessBackend>
        {
            new MockBrightnessBackend(),
            new MockBrightnessBackend(MockBrightnessBackend.UnsupportedOnlyDisplays(), name: "mock-unsupported"),
        };
        if (OperatingSystem.IsWindows())
            backends.Add(WindowsBrightnessBackend.CreateDefault());
        return backends;
    }
}