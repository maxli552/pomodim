using Pomodim.Core.Brightness;
using Xunit;

namespace Pomodim.Core.Tests;

public class MockBrightnessBackendTests
{
    [Fact]
    public void Default_displays_are_deterministic()
    {
        var a = new MockBrightnessBackend();
        var b = new MockBrightnessBackend();
        Assert.Equal(a.GetDisplays(), b.GetDisplays());
    }

    [Fact]
    public void Default_set_contains_an_unsupported_display_with_explicit_reason()
    {
        var displays = new MockBrightnessBackend().GetDisplays();
        var unsupported = displays.Single(d => !d.Supported);
        Assert.False(string.IsNullOrWhiteSpace(unsupported.Reason));
        Assert.Contains("no backlight control", unsupported.Reason);
    }

    [Theory]
    [InlineData(0, 0)]     // at min -> min
    [InlineData(-20, 0)]   // below min -> min
    [InlineData(100, 100)] // at max -> max
    [InlineData(140, 100)] // above max -> max
    [InlineData(42, 42)]   // in range -> value
    public void Set_brightness_clamps_to_range(int requested, int expected)
    {
        var backend = new MockBrightnessBackend();
        backend.SetBrightness("mock-1", requested);
        var display = backend.GetDisplays().Single(d => d.Id == "mock-1");
        Assert.Equal(expected, display.Current);
    }

    [Fact]
    public void Set_brightness_clamps_to_each_display_own_range()
    {
        var backend = new MockBrightnessBackend();
        backend.SetBrightness("mock-2", 5);
        Assert.Equal(10, backend.GetDisplays().Single(d => d.Id == "mock-2").Current);
        backend.SetBrightness("mock-2", 95);
        Assert.Equal(90, backend.GetDisplays().Single(d => d.Id == "mock-2").Current);
    }

    [Fact]
    public void Set_brightness_records_calls_with_requested_and_applied_values()
    {
        var backend = new MockBrightnessBackend();
        backend.SetBrightness("mock-1", -20);
        backend.SetBrightness("mock-2", 50);

        var calls = backend.Calls;
        Assert.Equal(2, calls.Count);
        Assert.Equal(new SetCall("mock-1", -20, 0), calls[0]);
        Assert.Equal(new SetCall("mock-2", 50, 50), calls[1]);
    }

    [Fact]
    public void Set_brightness_on_unsupported_display_throws_with_reason()
    {
        var backend = new MockBrightnessBackend();
        var ex = Assert.Throws<NotSupportedException>(() => backend.SetBrightness("mock-3", 50));
        Assert.Contains("no backlight control", ex.Message);
    }

    [Fact]
    public void Set_brightness_on_unknown_id_throws()
    {
        var backend = new MockBrightnessBackend();
        Assert.Throws<KeyNotFoundException>(() => backend.SetBrightness("nope", 50));
    }

    [Fact]
    public void Snapshot_captures_current_values()
    {
        var backend = new MockBrightnessBackend();
        backend.SetBrightness("mock-1", 33);

        var snapshot = backend.Snapshot();

        Assert.Equal("mock", snapshot.BackendName);
        Assert.Equal(33, snapshot.Values["mock-1"]);
        Assert.Equal(50, snapshot.Values["mock-2"]);
    }

    [Fact]
    public void Restore_returns_every_display_to_its_snapshotted_value()
    {
        var backend = new MockBrightnessBackend();
        var snapshot = backend.Snapshot();

        backend.SetBrightness("mock-1", 10);
        backend.SetBrightness("mock-2", 80);

        backend.Restore(snapshot);

        var displays = backend.GetDisplays().ToDictionary(d => d.Id);
        foreach (var (id, value) in snapshot.Values)
            Assert.Equal(value, displays[id].Current);
        Assert.Equal(80, displays["mock-1"].Current);
        Assert.Equal(50, displays["mock-2"].Current);
    }

    [Fact]
    public void Restore_rejects_a_snapshot_from_another_backend()
    {
        var backend = new MockBrightnessBackend();
        var foreign = new BrightnessSnapshot("win-ddc", new Dictionary<string, int> { ["mock-1"] = 50 });
        Assert.Throws<ArgumentException>(() => backend.Restore(foreign));
    }
}