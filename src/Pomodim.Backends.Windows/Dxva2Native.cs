using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Pomodim.Backends.Windows;

/// <summary>One HMONITOR as <c>EnumDisplayMonitors</c> reports it.</summary>
/// <param name="HMonitor">The HMONITOR handle.</param>
/// <param name="Device">The <c>szDevice</c> name from <c>GetMonitorInfo</c> ("\\.\DISPLAY1").</param>
internal readonly record struct NativeMonitor(IntPtr HMonitor, string Device);

/// <summary>One physical monitor handle plus the description Dxva2 reports for it.</summary>
internal readonly record struct NativePhysicalMonitor(IntPtr Handle, string Description);

/// <summary>
/// The result of <c>GetVCPFeatureAndVCPFeatureReply</c>. <see cref="Succeeded"/> is the
/// decoded <c>BOOL</c>; <see cref="LastError"/> is only meaningful when it is false.
/// The values are in the documented native order: current first, then maximum.
/// </summary>
internal readonly record struct VcpReply(
    bool Succeeded,
    int CodeType,
    uint Current,
    uint Max,
    int LastError);

/// <summary>
/// The raw Dxva2/user32 calls, one method per native function. This is the seam the
/// handle-level tests replace with a fake: <see cref="Dxva2Native"/> is the only
/// implementation that touches a <c>DllImport</c>.
/// </summary>
internal interface IDxva2Native
{
    /// <summary>Every HMONITOR, in enumeration order.</summary>
    IReadOnlyList<NativeMonitor> EnumMonitors();

    /// <summary>
    /// Acquires the physical-monitor handles for one HMONITOR. The caller owns the
    /// handles and must hand them back to <see cref="DestroyPhysicalMonitors"/>.
    /// </summary>
    IReadOnlyList<NativePhysicalMonitor> GetPhysicalMonitors(IntPtr hMonitor);

    /// <summary>
    /// Reads one VCP feature. <c>pvct</c>, <c>current</c> and <c>max</c> are the native
    /// out-parameters in documented order (type, current value, maximum value).
    /// </summary>
    VcpReply GetVcpFeature(IntPtr hPhysicalMonitor, byte vcpCode);

    /// <summary>Writes one VCP feature. False means failure; <paramref name="lastError"/>
    /// then carries <c>GetLastWin32Error()</c>.</summary>
    bool SetVcpFeature(IntPtr hPhysicalMonitor, byte vcpCode, uint value, out int lastError);

    /// <summary>Releases physical-monitor handles previously returned by
    /// <see cref="GetPhysicalMonitors"/>.</summary>
    void DestroyPhysicalMonitors(IReadOnlyList<IntPtr> handles);
}

/// <summary>
/// The real Dxva2 layer. Signatures follow the Windows SDK:
/// <list type="bullet">
/// <item><c>GetPhysicalMonitorsFromHMONITOR(HMONITOR, DWORD, LPPHYSICAL_MONITOR, LPDWORD)</c> → BOOL</item>
/// <item><c>GetVCPFeatureAndVCPFeatureReply(HANDLE, BYTE, LPMC_VCP_CODE_TYPE, LPDWORD current, LPDWORD max)</c> → BOOL</item>
/// <item><c>SetVCPFeature(HANDLE, BYTE, DWORD)</c> → BOOL</item>
/// <item><c>DestroyPhysicalMonitors(DWORD, LPPHYSICAL_MONITOR)</c> → BOOL</item>
/// </list>
/// Every <c>BOOL</c> is read the Win32 way: nonzero is success. Failures carry
/// <c>GetLastWin32Error()</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Dxva2Native : IDxva2Native
{
    public IReadOnlyList<NativeMonitor> EnumMonitors()
    {
        if (!OperatingSystem.IsWindows())
            return []; // user32/Dxva2 do not exist off-Windows; nothing to enumerate

        var monitors = new List<NativeMonitor>();
        var callback = new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcScreen, ref RECT rect, IntPtr data) =>
        {
            monitors.Add(new NativeMonitor(hMonitor, DeviceName(hMonitor)));
            return true;
        });
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        finally
        {
            GC.KeepAlive(callback); // the delegate must outlive the native call
        }
        return monitors;
    }

    public IReadOnlyList<NativePhysicalMonitor> GetPhysicalMonitors(IntPtr hMonitor)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        // The first call only asks for the count: with a zero-sized array it returns
        // FALSE with ERROR_INSUFFICIENT_BUFFER while still filling pdwCount, so the
        // BOOL is deliberately ignored here and the count is what matters.
        _ = GetPhysicalMonitorsFromHMONITOR(hMonitor, 0, IntPtr.Zero, out var count);
        if (count == 0)
            return []; // never index monitors[0] of an empty array

        var monitors = new PHYSICAL_MONITOR[count];
        if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors, out var acquired))
            return [];
        if (acquired > count)
            acquired = count; // defensive: never read past the array we allocated

        var result = new List<NativePhysicalMonitor>((int)acquired);
        for (var i = 0; i < acquired; i++)
            result.Add(new NativePhysicalMonitor(monitors[i].hPhysicalMonitor, monitors[i].szPhysicalMonitorDescription ?? string.Empty));
        return result;
    }

    public VcpReply GetVcpFeature(IntPtr hPhysicalMonitor, byte vcpCode)
    {
        var succeeded = GetVCPFeatureAndVCPFeatureReply(
            hPhysicalMonitor, vcpCode, out var codeType, out var current, out var max);
        var lastError = succeeded ? 0 : Marshal.GetLastWin32Error();
        return new VcpReply(succeeded, codeType, current, max, lastError);
    }

    public bool SetVcpFeature(IntPtr hPhysicalMonitor, byte vcpCode, uint value, out int lastError)
    {
        var succeeded = SetVCPFeature(hPhysicalMonitor, vcpCode, value);
        lastError = succeeded ? 0 : Marshal.GetLastWin32Error();
        return succeeded;
    }

    public void DestroyPhysicalMonitors(IReadOnlyList<IntPtr> handles)
    {
        if (handles.Count == 0)
            return;
        var monitors = new PHYSICAL_MONITOR[handles.Count];
        for (var i = 0; i < handles.Count; i++)
            monitors[i] = new PHYSICAL_MONITOR { hPhysicalMonitor = handles[i] };
        DestroyPhysicalMonitors((uint)monitors.Length, monitors);
    }

    /// <summary>sizeof(MONITORINFOEXW) in the SDK: 104 bytes. Pinned by a test.</summary>
    internal static int MonitorInfoExSize => Marshal.SizeOf<MONITORINFOEX>();

    private static string DeviceName(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX { cbSize = (uint)MonitorInfoExSize };
        if (!GetMonitorInfo(hMonitor, ref info))
            return string.Empty;
        return info.szDevice ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    /// <summary>
    /// <c>MONITORINFOEXW</c> is exactly: cbSize, rcMonitor, rcWork, dwFlags, szDevice[32].
    /// Extra fields here would inflate <c>Marshal.SizeOf</c>, and <c>GetMonitorInfo</c>
    /// rejects a cbSize that is not sizeof(MONITORINFOEXW) — which silently costs the
    /// stable <c>szDevice</c> id on real hardware.
    /// </summary>
    /// <summary>
    /// The SDK's <c>PHYSICAL_MONITOR</c>: a handle and a 128-character description.
    /// (Not <c>MONITORINFOEX</c> — that is what <c>GetMonitorInfo</c> fills.)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? szPhysicalMonitorDescription;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcScreen, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("Dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint dwArraySize, IntPtr lpPhysicalMonitorArray, out uint pdwPhysicalMonitorCount);

    [DllImport("Dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint dwArraySize, [Out] PHYSICAL_MONITOR[] lpPhysicalMonitorArray, out uint pdwPhysicalMonitorCount);

    [DllImport("Dxva2.dll", EntryPoint = "GetVCPFeatureAndVCPFeatureReply", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr hMonitor,
        byte dwVcpCode,
        out int pvct,
        out uint pdwCurrentValue,
        out uint pdwMaximumValue);

    [DllImport("Dxva2.dll", EntryPoint = "SetVCPFeature", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr hMonitor, byte dwVcpCode, uint dwNewValue);

    [DllImport("Dxva2.dll", EntryPoint = "DestroyPhysicalMonitors", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] lpPhysicalMonitorArray);
}
