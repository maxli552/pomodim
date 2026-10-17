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

    public IReadOnlyList<PhysicalMonitor> GetDisplays() =>
        _states.Select(s => s.Monitor with { Current = s.Current }).ToList();

    public void SetVcpBrightness(IntPtr handle, int value)
    {
        var state = _states.FirstOrDefault(s => s.Monitor.Handle == handle)
            ?? throw new KeyNotFoundException($"Unknown monitor handle {handle}.");
        state.Current = value;
        Calls.Add((handle, value));
    }
}

/// <summary>
/// A fake WMI fallback: one fixed panel, records every set.
/// </summary>
internal sealed class FakeWmiFallback : WmiBrightnessFallback
{
    public List<int> Sets { get; } = [];

    public override IReadOnlyList<Panel> GetPanels() =>
        [new Panel("panel-1", 60, 0, 100)];

    public override void SetBrightness(string id, int value) => Sets.Add(value);
}

public class WindowsBrightnessBackendTests
{
    [Fact]
    public void SetBrightness_ClampsToTheDisplaysOwnRange()
    {
        var handle = new IntPtr(0x1000);
        var api = new FakeMonitorApi(
            new PhysicalMonitor(handle, "Panel A", 10, 90, 50, true, null));
        var backend = new WindowsBrightnessBackend(api);
        var id = backend.GetDisplays()[0].Id;

        backend.SetBrightness(id, 5);   // below min -> min
        backend.SetBrightness(id, 95);  // above max -> max
        backend.SetBrightness(id, 42);  // in range -> value

        Assert.Equal([(handle, 10), (handle, 90), (handle, 42)], api.Calls);
        Assert.Equal(42, backend.GetDisplays()[0].Current);
    }

    [Fact]
    public void UnsupportedDisplay_ReportsReason_AndRefusesSet()
    {
        var handle = new IntPtr(0x2000);
        var api = new FakeMonitorApi(
            new PhysicalMonitor(
                handle, "Panel B", 0, 100, 0, false,
                "monitor does not support the VCP brightness feature (0x10)"));
        var backend = new WindowsBrightnessBackend(api);

        var display = backend.GetDisplays()[0];
        Assert.False(display.Supported);
        Assert.Equal("monitor does not support the VCP brightness feature (0x10)", display.Reason);

        Assert.Throws<NotSupportedException>(() => backend.SetBrightness(display.Id, 50));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Snapshot_Restore_RoundTripsEveryDisplay()
    {
        var h1 = new IntPtr(0x1000);
        var h2 = new IntPtr(0x2000);
        var api = new FakeMonitorApi(
            new PhysicalMonitor(h1, "Panel A", 0, 100, 80, true, null),
            new PhysicalMonitor(h2, "Panel B", 10, 90, 50, true, null));
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
        var handle = new IntPtr(0x3000);
        var api = new FakeMonitorApi(
            new PhysicalMonitor(
                handle, "Internal Panel", 0, 100, 0, false,
                "monitor does not support the VCP brightness feature (0x10)"));
        var wmi = new FakeWmiFallback();
        var backend = new WindowsBrightnessBackend(api, wmi);

        var display = backend.GetDisplays()[0];
        Assert.True(display.Supported);
        Assert.Equal("windows-wmi", display.BackendName);
        Assert.Equal(60, display.Current);

        backend.SetBrightness(display.Id, 30);
        Assert.Equal([30], wmi.Sets);
        Assert.Empty(api.Calls);
    }
}