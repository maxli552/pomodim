using Pomodim.Backends.Windows;
using Pomodim.Core.Brightness;

namespace Pomodim.Backends.Windows.Tests;

/// <summary>
/// A fake <see cref="IDxva2Native"/>: in-memory HMONITORs and physical monitors that
/// answer VCP reads from their own state and record every native call, so the
/// <see cref="WindowsMonitorApi"/> layer is testable on any OS.
/// </summary>
internal sealed class FakeDxva2Native : IDxva2Native
{
    internal sealed class FakePhysicalMonitor
    {
        public required IntPtr Handle { get; init; }

        /// <summary>szPhysicalMonitorDescription, as Dxva2 reports it.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Number of consecutive VCP reads that fail before one succeeds.</summary>
        public int ReadsToFail { get; set; }

        public int ReadError { get; set; } = 31;

        public uint Current { get; set; }

        public uint Max { get; set; } = 100;

        public int CodeType { get; set; } = VcpBrightness.SetParameter;

        public bool SetSucceeds { get; set; } = true;

        public int SetError { get; set; } = 5;
    }

    internal sealed class FakeHMonitor
    {
        public required IntPtr Handle { get; init; }

        /// <summary>szDevice from GetMonitorInfo; empty when the fake has no name.</summary>
        public string Device { get; init; } = string.Empty;

        public List<FakePhysicalMonitor> Monitors { get; } = [];
    }

    public List<FakeHMonitor> HMonitors { get; } = [];

    /// <summary>Every native call in order: "get:&lt;handle&gt;", "set:&lt;handle&gt;=&lt;value&gt;",
    /// "destroy:&lt;handle&gt;".</summary>
    public List<string> Log { get; } = [];

    public List<(IntPtr Handle, byte Code, uint Value)> SetCalls { get; } = [];

    public List<(IntPtr Handle, byte Code)> GetCalls { get; } = [];

    public List<IntPtr> DestroyedHandles { get; } = [];

    public FakeHMonitor AddMonitor(string device, params FakePhysicalMonitor[] monitors)
    {
        var monitor = new FakeHMonitor { Handle = NextHandle(), Device = device };
        monitor.Monitors.AddRange(monitors);
        HMonitors.Add(monitor);
        return monitor;
    }

    public static FakePhysicalMonitor Physical(
        uint current,
        uint max = 100,
        string description = "",
        int readsToFail = 0) =>
        new()
        {
            Handle = NextHandle(),
            Description = description,
            Current = current,
            Max = max,
            ReadsToFail = readsToFail,
        };

    public IReadOnlyList<NativeMonitor> EnumMonitors() =>
        HMonitors.Select(m => new NativeMonitor(m.Handle, m.Device)).ToList();

    public IReadOnlyList<NativePhysicalMonitor> GetPhysicalMonitors(IntPtr hMonitor)
    {
        var monitor = HMonitors.FirstOrDefault(m => m.Handle == hMonitor);
        if (monitor is null)
            return [];
        return monitor.Monitors.Select(p => new NativePhysicalMonitor(p.Handle, p.Description)).ToList();
    }

    public VcpReply GetVcpFeature(IntPtr hPhysicalMonitor, byte vcpCode)
    {
        Log.Add($"get:0x{hPhysicalMonitor:x}");
        GetCalls.Add((hPhysicalMonitor, vcpCode));
        var monitor = Find(hPhysicalMonitor);
        if (monitor.ReadsToFail != 0)
        {
            monitor.ReadsToFail = monitor.ReadsToFail < 0 ? -1 : monitor.ReadsToFail - 1;
            return new VcpReply(false, 0, 0, 0, monitor.ReadError);
        }
        return new VcpReply(true, monitor.CodeType, monitor.Current, monitor.Max, 0);
    }

    public bool SetVcpFeature(IntPtr hPhysicalMonitor, byte vcpCode, uint value, out int lastError)
    {
        Log.Add($"set:0x{hPhysicalMonitor:x}={value}");
        SetCalls.Add((hPhysicalMonitor, vcpCode, value));
        var monitor = Find(hPhysicalMonitor);
        lastError = monitor.SetSucceeds ? 0 : monitor.SetError;
        return monitor.SetSucceeds;
    }

    public void DestroyPhysicalMonitors(IReadOnlyList<IntPtr> handles)
    {
        foreach (var handle in handles)
        {
            Log.Add($"destroy:0x{handle:x}");
            DestroyedHandles.Add(handle);
        }
    }

    private FakePhysicalMonitor Find(IntPtr handle) =>
        HMonitors.SelectMany(m => m.Monitors).FirstOrDefault(p => p.Handle == handle)
        ?? throw new InvalidOperationException($"Fake: no physical monitor for handle 0x{handle:x}.");

    private static long _nextHandle = 0x1000;

    private static IntPtr NextHandle() => new(Interlocked.Add(ref _nextHandle, 0x10));
}
