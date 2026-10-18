namespace Pomodim.Backends.Windows.Tests;

/// <summary>
/// MONITORINFOEXW is cbSize + two RECTs + dwFlags + szDevice[32] = 104 bytes. If a field is
/// added to the managed struct, Marshal.SizeOf grows and GetMonitorInfo starts failing, which
/// silently costs the stable szDevice id — this test is the guard against that regression.
/// </summary>
public class MonitorInfoLayoutTests
{
    [Fact]
    public void MonitorInfoEx_is_the_sdk_size() => Assert.Equal(104, Dxva2Native.MonitorInfoExSize);
}
