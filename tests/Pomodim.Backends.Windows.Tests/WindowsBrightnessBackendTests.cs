using Pomodim.Backends.Windows;

namespace Pomodim.Backends.Windows.Tests;

/// <summary>
/// A fake <see cref="IMonitorApi"/>: in-memory DDC/CI monitors that record every
/// <c>SetVcpBrightness</c> call and report the updated value on the next query,
/// like the real VCP read does. The backend logic is testable without hardware.
/// </summary>
internal sealed class FakeMonitorApi : IMonitorApi
{
    private sealed class State
    {
        public required PhysicalMonitor Monitor { get; init; }
        public int Current { get; set; }
    }

    private readonly List<State> _states;

    public FakeMonitorApi(params PhysicalMonitor[] monitors)
    {
        _states = monitors
            .Select(m => new State { Monitor = m, Current = m.Current })
            .ToList();
    }

    /// <summary>Every VCP write issued through the seam, in order.</summary>
    public List<(IntPtr Handle, int Value)> Calls { get; } = [];

    public bool Disposed { get; private set; }

    public IReadOnlyList<PhysicalMonitor> GetDisplays() =>
        _states.Select(s => s.Monitor with { Current = s.Current }).ToList();

    public void SetVcpBrightness(IntPtr handle, int value)
    {
        var state = _states.FirstOrDefault(s => s.Monitor.Handle == handle)
            ?? throw new KeyNotFoundException($"Unknown monitor handle {handle}.");
        state.Current = value;
        Calls.Add((handle, value));
    }

    public void Dispose() => Disposed = true;
}

/// <summary>An <see cref="IMonitorApi"/> whose enumeration always throws.</summary>
internal sealed class ThrowingMonitorApi : IMonitorApi
{
    public IReadOnlyList<PhysicalMonitor> GetDisplays() =>
        throw new InvalidOperationException("EnumDisplayMonitors failed (Win32 error 5)");

    public void SetVcpBrightness(IntPtr handle, int value) =>
        throw new InvalidOperationException("SetVCPFeature failed (Win32 error 5)");

    public void Dispose()
    {
    }
}

/// <summary>A fake WMI fallback: canned panels and a recorded set list.</summary>
internal sealed class FakeWmiFallback : IWmiBrightness
{
    public List<WmiBrightnessPanel> Panels { get; set; } = [new("panel-1", 60, 0, 100)];

    public List<(string Id, int Value)> Sets { get; } = [];

    /// <summary>When set, <see cref="GetPanels"/> throws it (a broken WMI provider).</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<WmiBrightnessPanel> GetPanels() =>
        Failure is null ? Panels : throw Failure;

    public void SetBrightness(string id, int value) => Sets.Add((id, value));
}

public class WindowsBrightnessBackendTests
{
    private static PhysicalMonitor DdcMonitor(IntPtr handle, string id, string name, int min, int max, int current) =>
        new(id, handle, name, min, max, current, true, null);

    private static PhysicalMonitor UnsupportedMonitor(IntPtr handle, string id, string name, string reason) =>
        new(id, handle, name, 0, 100, 0, false, reason);

    [Fact]
    public void SetBrightness_ClampsToTheDisplaysOwnRange()
    {
        var handle = new IntPtr(0x1000);
        var api = new FakeMonitorApi(DdcMonitor(handle, "ddc-display1", "Panel A", 10, 90, 50));
        var backend = new WindowsBrightnessBackend(api);
        var id = backend.GetDisplays()[0].Id;

        backend.SetBrightness(id, 5);   // below min -> min
        backend.SetBrightness(id, 95);  // above max -> max
        backend.SetBrightness(id, 42);  // in range -> value

        Assert.Equal([(handle, 10), (handle, 90), (handle, 42)], api.Calls);
        Assert.Equal(42, backend.GetDisplays()[0].Current);
    }

    [Fact]
    public void DdcDisplay_UsesTheMonitorIdAndBackend()
    {
        var api = new FakeMonitorApi(
            DdcMonitor(new IntPtr(0x1000), "ddc-display1", "Panel A", 0, 100, 80));
        var backend = new WindowsBrightnessBackend(api);

        var display = Assert.Single(backend.GetDisplays());

        Assert.Equal("ddc-display1", display.Id);
        Assert.Equal("windows-ddc", display.BackendName);
        Assert.Equal(80, display.Current);
    }

    [Fact]
    public void UnsupportedDisplay_ReportsReason_AndRefusesSet()
    {
        var reason = "monitor does not support the VCP brightness feature (0x10)";
        var api = new FakeMonitorApi(
            UnsupportedMonitor(new IntPtr(0x2000), "ddc-display2", "Panel B", reason));
        var backend = new WindowsBrightnessBackend(api);

        var display = backend.GetDisplays()[0];
        Assert.False(display.Supported);
        Assert.Equal(reason, display.Reason);

        Assert.Throws<NotSupportedException>(() => backend.SetBrightness(display.Id, 50));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Snapshot_Restore_RoundTripsEveryDisplay()
    {
        var h1 = new IntPtr(0x1000);
        var h2 = new IntPtr(0x2000);
        var api = new FakeMonitorApi(
            DdcMonitor(h1, "ddc-display1", "Panel A", 0, 100, 80),
            DdcMonitor(h2, "ddc-display2", "Panel B", 10, 90, 50));
        var backend = new WindowsBrightnessBackend(api);
        var displays = backend.GetDisplays();

        var snapshot = backend.Snapshot();
        Assert.Equal(80, snapshot.Values[displays[0].Id]);
        Assert.Equal(50, snapshot.Values[displays[1].Id]);

        backend.SetBrightness(displays[0].Id, 20);
        backend.SetBrightness(displays[1].Id, 70);

        backend.Restore(snapshot);

        Assert.Equal(80, backend.GetDisplays()[0].Current);
        Assert.Equal(50, backend.GetDisplays()[1].Current);
        Assert.Contains((h1, 80), api.Calls);
        Assert.Contains((h2, 50), api.Calls);
    }

    [Fact]
    public void NoDisplays_MeansNothingIsSet()
    {
        var api = new FakeMonitorApi();
        var backend = new WindowsBrightnessBackend(api);

        Assert.Empty(backend.GetDisplays());
        var snapshot = backend.Snapshot();
        Assert.Empty(snapshot.Values);

        backend.Restore(snapshot);
        Assert.Empty(api.Calls);

        Assert.Throws<KeyNotFoundException>(() => backend.SetBrightness("ddc-1", 50));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void DdcUnsupportedDisplay_FallsBackToWmi()
    {
        var api = new FakeMonitorApi(
            UnsupportedMonitor(
                new IntPtr(0x3000), "ddc-display1", "Internal Panel",
                "monitor does not support the VCP brightness feature (0x10)"));
        var wmi = new FakeWmiFallback();
        var backend = new WindowsBrightnessBackend(api, wmi);

        var display = backend.GetDisplays()[0];
        Assert.True(display.Supported);
        Assert.Equal("windows-wmi", display.BackendName);
        Assert.Equal(60, display.Current);

        backend.SetBrightness(display.Id, 30);
        Assert.Equal([("panel-1", 30)], wmi.Sets);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void WmiPanelIsMatchedToTheFirstDdcUnsupportedMonitor_NotByAllMonitorIndex()
    {
        // Round-5 defect: the WMI panel used to be looked up by the all-monitor index,
        // so an external DDC display listed first consumed the laptop panel's entry.
        var ddcHandle = new IntPtr(0x1000);
        var panelHandle = new IntPtr(0x3000);
        var api = new FakeMonitorApi(
            DdcMonitor(ddcHandle, "ddc-display1", "External", 0, 100, 70),
            UnsupportedMonitor(panelHandle, "ddc-display2", "Internal Panel", "no VCP brightness"));
        var wmi = new FakeWmiFallback { Panels = [new("panel-1", 45, 20, 100)] };
        var backend = new WindowsBrightnessBackend(api, wmi);

        var displays = backend.GetDisplays();

        Assert.Equal(2, displays.Count);
        Assert.Equal("windows-ddc", displays[0].BackendName);
        Assert.Equal(70, displays[0].Current);
        Assert.Equal("windows-wmi", displays[1].BackendName);
        Assert.Equal("Internal Panel", displays[1].Name);
        Assert.Equal(45, displays[1].Current);
        Assert.Equal(20, displays[1].Min);
        Assert.Equal(100, displays[1].Max);

        backend.SetBrightness(displays[1].Id, 55);
        Assert.Equal([("panel-1", 55)], wmi.Sets);
    }

    [Fact]
    public void WmiPanelsAreOnlyMatchedToDdcUnsupportedMonitors()
    {
        var api = new FakeMonitorApi(
            DdcMonitor(new IntPtr(0x1000), "ddc-display1", "External", 0, 100, 70),
            DdcMonitor(new IntPtr(0x2000), "ddc-display2", "Second external", 0, 100, 60),
            UnsupportedMonitor(new IntPtr(0x3000), "ddc-display3", "Internal Panel", "no VCP brightness"));
        var wmi = new FakeWmiFallback();
        var backend = new WindowsBrightnessBackend(api, wmi);

        var displays = backend.GetDisplays();

        Assert.Equal("windows-ddc", displays[0].BackendName);
        Assert.Equal("windows-ddc", displays[1].BackendName);
        Assert.Equal("windows-wmi", displays[2].BackendName);
        Assert.Equal(60, displays[2].Current);
    }

    [Fact]
    public void WithoutAWmiPanel_TheDdcReasonIsKept()
    {
        var reason = "monitor does not support the VCP brightness feature (0x10)";
        var api = new FakeMonitorApi(
            UnsupportedMonitor(new IntPtr(0x2000), "ddc-display1", "Panel B", reason));
        var wmi = new FakeWmiFallback { Panels = [] };
        var backend = new WindowsBrightnessBackend(api, wmi);

        var display = backend.GetDisplays()[0];

        Assert.False(display.Supported);
        Assert.StartsWith(reason, display.Reason, StringComparison.Ordinal);
        Assert.Contains("no WMI brightness panel available", display.Reason);
    }

    [Fact]
    public void ThrowingWmi_YieldsUnsupportedDisplay_KeepingTheDdcReason()
    {
        var reason = "the monitor did not answer the DDC/CI request (Win32 error 31)";
        var api = new FakeMonitorApi(
            UnsupportedMonitor(new IntPtr(0x2000), "ddc-display1", "Panel B", reason));
        var wmi = new FakeWmiFallback { Failure = new InvalidOperationException("WMI provider unavailable") };
        var backend = new WindowsBrightnessBackend(api, wmi);

        var display = backend.GetDisplays()[0];

        Assert.False(display.Supported);
        Assert.StartsWith(reason, display.Reason, StringComparison.Ordinal);
        Assert.Contains("WMI provider unavailable", display.Reason);
    }

    [Fact]
    public void ThrowingMonitorApi_YieldsUnsupportedDisplaysWithReasons_InsteadOfThrowing()
    {
        var backend = new WindowsBrightnessBackend(new ThrowingMonitorApi(), new FakeWmiFallback());

        var displays = backend.GetDisplays();

        var display = Assert.Single(displays);
        Assert.False(display.Supported);
        Assert.Contains("display enumeration failed", display.Reason);
        Assert.Contains("Win32 error 5", display.Reason);
        Assert.Equal("windows", display.BackendName);
    }

    [Fact]
    public void ThrowingMonitorApi_MakesSetAndSnapshotSafeButNotSilent()
    {
        var backend = new WindowsBrightnessBackend(new ThrowingMonitorApi());

        Assert.Throws<KeyNotFoundException>(() => backend.SetBrightness("ddc-display1", 40));

        // The snapshot still lists the enumeration error display (there is nothing to
        // restore, so Restore must not try to write to it).
        var snapshot = backend.Snapshot();
        Assert.Equal(0, Assert.Single(snapshot.Values).Value);
        backend.Restore(snapshot);
    }

    [Fact]
    public void Dispose_ReleasesTheMonitorApi()
    {
        var api = new FakeMonitorApi(DdcMonitor(new IntPtr(0x1000), "ddc-display1", "Panel A", 0, 100, 80));
        var backend = new WindowsBrightnessBackend(api);
        _ = backend.GetDisplays();

        backend.Dispose();

        Assert.True(api.Disposed);
        Assert.Throws<ObjectDisposedException>(() => backend.GetDisplays());
    }
}
