using System.Runtime.Versioning;
using Pomodim.Core.Brightness;

namespace Pomodim.Backends.Windows;

/// <summary>
/// The real DDC/CI seam: <see cref="IDxva2Native"/> behind the display-level
/// <see cref="IMonitorApi"/> the backend uses. It owns every physical-monitor handle it
/// acquires: a handle is released only when its monitor disappears or when the seam is
/// disposed, so a handle the backend was handed never dies while a write can still use it.
///
/// Displays are identified by the monitor's <c>szDevice</c> name, not by the handle:
/// handles change between enumerations, device names do not.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMonitorApi : IMonitorApi
{
    private sealed record HeldMonitor(
        IntPtr HMonitor,
        IntPtr Handle,
        string Device,
        string Description,
        string Id);

    private readonly IDxva2Native _native;
    private readonly List<HeldMonitor> _held = [];
    private bool _disposed;

    public WindowsMonitorApi()
        : this(new Dxva2Native())
    {
    }

    internal WindowsMonitorApi(IDxva2Native native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    /// <summary>
    /// Reads every monitor's current brightness. Re-enumerates the HMONITORs so a
    /// hot-plugged display is picked up, but reuses the held handles of monitors that
    /// are still present (never destroys a handle that is still exposed).
    /// </summary>
    public IReadOnlyList<PhysicalMonitor> GetDisplays()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Sync();
        return _held.Select(Describe).ToList();
    }

    /// <summary>Writes VCP 0x10 on one monitor. A failed write throws with the Win32 error.</summary>
    public void SetVcpBrightness(IntPtr handle, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Brightness must not be negative.");
        if (!_held.Any(h => h.Handle == handle))
            throw new ArgumentException($"Unknown physical monitor handle 0x{handle:x}.", nameof(handle));

        if (!_native.SetVcpFeature(handle, VcpBrightness.Code, (uint)value, out var error))
            throw new IOException($"SetVCPFeature (VCP 0x10) failed: {VcpBrightness.DescribeWin32Error(error)}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_held.Count > 0)
            _native.DestroyPhysicalMonitors(_held.Select(h => h.Handle).ToList());
        _held.Clear();
    }

    /// <summary>
    /// Brings the held handle list in line with the HMONITORs that exist right now:
    /// acquire handles for monitors not held yet, release handles of monitors that
    /// disappeared. Handles of monitors present in both passes are left untouched.
    /// </summary>
    private void Sync()
    {
        var seen = new HashSet<IntPtr>();
        var position = 0;
        foreach (var monitor in _native.EnumMonitors())
        {
            if (!seen.Add(monitor.HMonitor))
                continue;
            position++;
            if (_held.Any(h => h.HMonitor == monitor.HMonitor))
                continue;

            var physical = _native.GetPhysicalMonitors(monitor.HMonitor);
            for (var i = 0; i < physical.Count; i++)
            {
                _held.Add(new HeldMonitor(
                    monitor.HMonitor,
                    physical[i].Handle,
                    monitor.Device,
                    physical[i].Description,
                    IdFor(monitor.Device, position, i)));
            }
        }

        for (var i = _held.Count - 1; i >= 0; i--)
        {
            if (seen.Contains(_held[i].HMonitor))
                continue;
            _native.DestroyPhysicalMonitors([_held[i].Handle]);
            _held.RemoveAt(i);
        }
    }

    private PhysicalMonitor Describe(HeldMonitor held)
    {
        var reading = ReadVcp(held.Handle);
        return new PhysicalMonitor(
            held.Id,
            held.Handle,
            DisplayName(held),
            reading.Min,
            reading.Max,
            reading.Current,
            reading.Supported,
            reading.Reason);
    }

    /// <summary>
    /// Reads VCP 0x10, retrying a failed capability read once: DDC/CI reads over I2C
    /// fail intermittently, and one retry removes most of the false "unsupported".
    /// </summary>
    private VcpReading ReadVcp(IntPtr handle)
    {
        var reply = _native.GetVcpFeature(handle, VcpBrightness.Code);
        if (!reply.Succeeded)
            reply = _native.GetVcpFeature(handle, VcpBrightness.Code);

        return reply.Succeeded
            ? VcpBrightness.Interpret(true, reply.CodeType, reply.Current, reply.Max, null)
            : VcpBrightness.Interpret(false, reply.CodeType, reply.Current, reply.Max, reply.LastError);
    }

    private static string DisplayName(HeldMonitor held)
    {
        if (!string.IsNullOrWhiteSpace(held.Description))
            return held.Description;
        return string.IsNullOrWhiteSpace(held.Device) ? "unknown display" : held.Device;
    }

    /// <summary>
    /// A stable display id from <c>szDevice</c> ("\\.\DISPLAY1" → "ddc-display1").
    /// The handle is deliberately not part of the id: it changes between enumerations.
    /// When the device name is missing, fall back to the enumeration position.
    /// </summary>
    private static string IdFor(string device, int monitorPosition, int physicalIndex)
    {
        var name = DeviceNameOf(device);
        if (name is null)
            return physicalIndex == 0 ? $"ddc-{monitorPosition}" : $"ddc-{monitorPosition}-{physicalIndex + 1}";
        return physicalIndex == 0 ? $"ddc-{name}" : $"ddc-{name}-{physicalIndex + 1}";
    }

    private static string? DeviceNameOf(string device)
    {
        var trimmed = device.Trim();
        if (trimmed.Length == 0)
            return null;
        var last = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(last) ? null : last.ToLowerInvariant();
    }
}
