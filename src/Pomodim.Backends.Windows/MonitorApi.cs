namespace Pomodim.Backends.Windows;

/// <summary>
/// One physical monitor as the DDC/CI layer sees it. <see cref="Id"/> is derived from
/// the monitor's <c>szDevice</c> name (index fallback), so it is stable across refreshes;
/// <see cref="Handle"/> is the Dxva2 physical-monitor handle used for VCP calls, kept
/// alive by <see cref="WindowsMonitorApi"/> until it is disposed. <see cref="Min"/>/
/// <see cref="Max"/>/<see cref="Current"/> are percentages. When
/// <see cref="DdcSupported"/> is false, <see cref="Reason"/> says why.
/// </summary>
internal sealed record PhysicalMonitor(
    string Id,
    IntPtr Handle,
    string Name,
    int Min,
    int Max,
    int Current,
    bool DdcSupported,
    string? Reason);

/// <summary>
/// The seam between the backend logic and the DDC/CI calls. The production
/// implementation is <see cref="WindowsMonitorApi"/> (Dxva2.dll through
/// <see cref="IDxva2Native"/>); tests substitute a fake so the backend logic runs
/// without hardware. Disposing the seam releases every physical-monitor handle it
/// acquired.
/// </summary>
internal interface IMonitorApi : IDisposable
{
    /// <summary>Every physical monitor, in a stable order.</summary>
    IReadOnlyList<PhysicalMonitor> GetDisplays();

    /// <summary>
    /// Sets VCP brightness (code 0x10) on one monitor. The value must already be
    /// clamped to the monitor's own [Min, Max] range by the caller.
    /// </summary>
    void SetVcpBrightness(IntPtr handle, int value);
}
