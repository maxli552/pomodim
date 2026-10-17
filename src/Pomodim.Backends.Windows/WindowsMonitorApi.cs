using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Pomodim.Backends.Windows;

/// <summary>
/// The real DDC/CI seam. Enumerates the HMONITORs with user32
/// <c>EnumDisplayMonitors</c>, gets the per-monitor handles with Dxva2
/// <c>GetPhysicalMonitorsFromHMONITOR</c>, reads brightness with
/// <c>GetVCPFeatureAndVCPFeatureReply</c> (VCP code 0x10), writes with
/// <c>SetVCPFeature</c>, and releases the handles with
/// <c>DestroyPhysicalMonitors</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMonitorApi : IMonitorApi
{
    private const uint VcpBrightness = 0x10;

    public IReadOnlyList<PhysicalMonitor> GetDisplays()
    {
        if (!OperatingSystem.IsWindows())
            return []; // Dxva2/user32 do not exist off-Windows; nothing to enumerate
        var result = new List<PhysicalMonitor>();
        var callback = new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcScreen, ref RECT rect, IntPtr data) =>
        {
            AddMonitors(result, hMonitor);
            return true;
        });
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return result;
    }

    public void SetVcpBrightness(IntPtr handle, int value)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DDC/CI (Dxva2) is only available on Windows.");
        var status = SetVCPFeature(handle, VcpBrightness, (uint)value);
        if (status != 0)
            throw new IOException($"SetVCPFeature (VCP 0x10) failed with status 0x{status:x8}.");
    }

    private static void AddMonitors(List<PhysicalMonitor> result, IntPtr hMonitor)
    {
        if (GetPhysicalMonitorsFromHMONITOR(hMonitor, 0, IntPtr.Zero, out var count) != 0 || count == 0)
            return;

        var monitors = new PHYSICAL_MONITOR[count];
        try
        {
            if (GetPhysicalMonitorsFromHMONITOR(hMonitor, (uint)count, ref monitors[0], out count) != 0)
                return;
            for (var i = 0; i < count; i++)
                result.Add(Describe(monitors[i]));
        }
        finally
        {
            DestroyPhysicalMonitors((uint)count, ref monitors[0]);
        }
    }

    private static PhysicalMonitor Describe(PHYSICAL_MONITOR monitor)
    {
        var name = string.IsNullOrWhiteSpace(monitor.MonitorInfo.szDescription)
            ? monitor.MonitorInfo.szDevice
            : monitor.MonitorInfo.szDescription;

        var status = GetVCPFeatureAndVCPFeatureReply(
            monitor.hPhysicalMonitor, VcpBrightness, out _, out var max, out var current);
        if (status == 0)
            return new PhysicalMonitor(monitor.hPhysicalMonitor, name, 0, (int)max, (int)current, true, null);

        // GetVCPFeatureAndVCPFeatureReply: 1 = feature not supported by the monitor,
        // 2 = the monitor does not support VCP at all, anything else = error.
        var reason = status switch
        {
            1 => "monitor does not support the VCP brightness feature (0x10)",
            2 => "monitor does not support DDC/CI (no VCP)",
            _ => $"GetVCPFeatureAndVCPFeatureReply failed (status 0x{status:x8})",
        };
        return new PhysicalMonitor(monitor.hPhysicalMonitor, name, 0, 100, 0, false, reason);
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
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szDescription;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szProduct;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        public MONITORINFOEX MonitorInfo;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcScreen, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("Dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
    private static extern int GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint dwArraySize, IntPtr lpPhysicalMonitorArray, out uint pdwPhysicalMonitorCount);

    [DllImport("Dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
    private static extern int GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint dwArraySize, ref PHYSICAL_MONITOR lpPhysicalMonitorArray, out uint pdwPhysicalMonitorCount);

    [DllImport("Dxva2.dll", EntryPoint = "GetVCPFeatureAndVCPFeatureReply", SetLastError = true)]
    private static extern int GetVCPFeatureAndVCPFeatureReply(
        IntPtr hMonitor, uint dwVcpFeature, out uint pdwVcpFeatureVersion, out uint pdwVcpFeatureMaxValue, out uint pdwVcpVcpFeatureValue);

    [DllImport("Dxva2.dll", EntryPoint = "SetVCPFeature", SetLastError = true)]
    private static extern int SetVCPFeature(IntPtr hMonitor, uint dwVcpFeature, uint dwVcpFeatureValue);

    [DllImport("Dxva2.dll", EntryPoint = "DestroyPhysicalMonitors", SetLastError = true)]
    private static extern int DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, ref PHYSICAL_MONITOR lpPhysicalMonitorArray);
}