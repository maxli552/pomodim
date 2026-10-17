using Pomodim.Backends.Windows;
using Pomodim.Core.Brightness;

namespace Pomodim.App.Services;

/// <summary>
/// Picks the backend for the GUI. Same rule as the CLI: the real hardware backend on
/// Windows, the mock everywhere else so the app still runs (and can be demoed) on a
/// machine without controllable displays.
/// </summary>
public static class BackendFactory
{
    public static IBrightnessBackend Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsBrightnessBackend.CreateDefault();
        }

        return new MockBrightnessBackend();
    }
}
