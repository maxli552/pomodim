using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace Pomodim.App.Services;

/// <summary>
/// "Start with Windows". HKCU only: no elevation, no service, nothing in Program Files.
/// Non-Windows platforms are not wired yet (LINUX/MACOS autostart is a TODO item).
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Pomodim";

    public static bool IsSupported => OperatingSystem.IsWindows();

    private static string ProcessPath =>
        Environment.ProcessPath
        ?? Assembly.GetEntryAssembly()?.Location
        ?? throw new InvalidOperationException("cannot determine the executable path");

    public static bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>Idempotent: writes the value again if it already exists.</summary>
    public static void Set(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ProcessPath}\" --tray");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
