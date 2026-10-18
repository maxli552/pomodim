using Pomodim.Backends.Windows;
using Pomodim.Core.Brightness;

namespace Pomodim.Backends.Windows.Tests;

/// <summary>
/// Handle-level tests for <see cref="WindowsMonitorApi"/> against a fake
/// <see cref="IDxva2Native"/>. They pin down the round-5 defects: every BOOL is read as
/// "nonzero = success", the VCP out-parameters are in documented order (current before
/// max), a failed capability read is retried once, and a handle is never destroyed while
/// it is still exposed.
/// </summary>
public class WindowsMonitorApiTests
{
    [Fact]
    public void SuccessfulSetVcp_DoesNotThrow_AndSendsTheByteVcpCode()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 70);
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        using var api = new WindowsMonitorApi(native);

        var display = api.GetDisplays().Single();
        api.SetVcpBrightness(display.Handle, 42);

        var call = Assert.Single(native.SetCalls);
        Assert.Equal(physical.Handle, call.Handle);
        Assert.Equal(VcpBrightness.Code, call.Code);   // 0x10, as a byte
        Assert.Equal(42u, call.Value);
    }

    [Fact]
    public void FailedSetVcp_ThrowsAReadableErrorWithTheWin32Code()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 70);
        physical.SetSucceeds = false;
        physical.SetError = 5;
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        using var api = new WindowsMonitorApi(native);
        var display = api.GetDisplays().Single();

        var error = Assert.Throws<IOException>(() => api.SetVcpBrightness(display.Handle, 42));

        Assert.Contains("Win32 error 5", error.Message);
        Assert.Contains("SetVCPFeature", error.Message);
    }

    [Fact]
    public void CapabilityRead_ReadsCurrentBeforeMax()
    {
        var native = new FakeDxva2Native();
        native.AddMonitor(@"\\.\DISPLAY1", FakeDxva2Native.Physical(current: 68, max: 100));
        using var api = new WindowsMonitorApi(native);

        var display = api.GetDisplays().Single();

        Assert.True(display.DdcSupported);
        Assert.Equal(100, display.Max);
        Assert.Equal(68, display.Current); // unswapped: 68 current, 100 max
        Assert.Equal(VcpBrightness.Code, native.GetCalls.Single().Code);
    }

    [Fact]
    public void CapabilityRead_Failure_IsUnsupportedWithTheWin32Reason()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 0, max: 0, readsToFail: -1);
        physical.ReadError = 31;
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        using var api = new WindowsMonitorApi(native);

        var display = api.GetDisplays().Single();

        Assert.False(display.DdcSupported);
        Assert.Contains("Win32 error 31", display.Reason);
        Assert.Equal(2, native.GetCalls.Count); // one retry, then give up
    }

    [Fact]
    public void CapabilityRead_IsRetriedOnce_WhenTheFirstReadFails()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 68, max: 100, readsToFail: 1);
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        using var api = new WindowsMonitorApi(native);

        var display = api.GetDisplays().Single();

        Assert.True(display.DdcSupported);
        Assert.Equal(100, display.Max);
        Assert.Equal(68, display.Current);
        Assert.Equal(2, native.GetCalls.Count);
    }

    [Fact]
    public void CapabilityRead_SecondFailure_IsNotRetriedAgain()
    {
        var native = new FakeDxva2Native();
        native.AddMonitor(@"\\.\DISPLAY1", FakeDxva2Native.Physical(current: 0, readsToFail: 2));
        using var api = new WindowsMonitorApi(native);

        var display = api.GetDisplays().Single();

        Assert.False(display.DdcSupported);
        Assert.Equal(2, native.GetCalls.Count);
    }

    [Fact]
    public void ZeroPhysicalMonitors_DoesNotThrow()
    {
        var native = new FakeDxva2Native();
        native.AddMonitor(@"\\.\DISPLAY1"); // HMONITOR with no physical monitors
        using var api = new WindowsMonitorApi(native);

        Assert.Empty(api.GetDisplays());
    }

    [Fact]
    public void Ids_ComeFromTheDeviceName_AndAreStableAcrossRefresh()
    {
        var native = new FakeDxva2Native();
        native.AddMonitor(@"\\.\DISPLAY1", FakeDxva2Native.Physical(current: 70));
        native.AddMonitor(@"\\.\DISPLAY2", FakeDxva2Native.Physical(current: 40));
        using var api = new WindowsMonitorApi(native);

        var first = api.GetDisplays();
        var second = api.GetDisplays();

        Assert.Equal(["ddc-display1", "ddc-display2"], first.Select(d => d.Id));
        Assert.Equal(first.Select(d => d.Id), second.Select(d => d.Id));
        Assert.Equal(first.Select(d => d.Handle), second.Select(d => d.Handle));
    }

    [Fact]
    public void Ids_FallBackToTheEnumerationIndex_WhenTheDeviceNameIsMissing()
    {
        var native = new FakeDxva2Native();
        native.AddMonitor(string.Empty, FakeDxva2Native.Physical(current: 30));
        using var api = new WindowsMonitorApi(native);

        Assert.Equal("ddc-1", api.GetDisplays().Single().Id);
    }

    [Fact]
    public void Handles_AreNotDestroyedWhileTheyAreStillExposed()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 70);
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        using var api = new WindowsMonitorApi(native);

        var first = api.GetDisplays().Single();
        api.SetVcpBrightness(first.Handle, 30);
        var second = api.GetDisplays().Single();
        api.SetVcpBrightness(second.Handle, 90);

        Assert.Empty(native.DestroyedHandles);
        Assert.DoesNotContain(native.Log, entry => entry.StartsWith("destroy", StringComparison.Ordinal));
        Assert.StartsWith("set:", native.Log[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_DestroysEveryHandle_ExactlyOnce()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 70);
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        var api = new WindowsMonitorApi(native);
        _ = api.GetDisplays();

        api.Dispose();
        api.Dispose();

        Assert.Equal([physical.Handle], native.DestroyedHandles);
    }

    [Fact]
    public void AfterDispose_ReadsAndWritesThrow()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 70);
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        var api = new WindowsMonitorApi(native);
        var display = api.GetDisplays().Single();
        api.Dispose();

        Assert.Throws<ObjectDisposedException>(() => api.GetDisplays());
        Assert.Throws<ObjectDisposedException>(() => api.SetVcpBrightness(display.Handle, 10));
    }

    [Fact]
    public void UnknownHandle_IsRefusedBeforeTheNativeWrite()
    {
        var native = new FakeDxva2Native();
        native.AddMonitor(@"\\.\DISPLAY1", FakeDxva2Native.Physical(current: 70));
        using var api = new WindowsMonitorApi(native);
        _ = api.GetDisplays();

        Assert.Throws<ArgumentException>(() => api.SetVcpBrightness(new IntPtr(0xdead), 10));
        Assert.Empty(native.SetCalls);
    }

    [Fact]
    public void UnpluggingAMonitor_ReleasesItsHandle()
    {
        var native = new FakeDxva2Native();
        var physical = FakeDxva2Native.Physical(current: 70);
        native.AddMonitor(@"\\.\DISPLAY1", physical);
        using var api = new WindowsMonitorApi(native);
        Assert.Single(api.GetDisplays());

        native.HMonitors.Clear(); // the display was unplugged
        Assert.Empty(api.GetDisplays());

        Assert.Equal([physical.Handle], native.DestroyedHandles);
    }
}
