using Pomodim.Core.Brightness;
using Xunit;

namespace Pomodim.Core.Tests;

/// <summary>
/// The VCP interpretation is pure logic, so every branch is testable on any OS.
/// The values below mirror what Dxva2 returns: (pvct, current, max) in documented order.
/// </summary>
public class VcpBrightnessTests
{
    [Fact]
    public void Success_KeepsCurrentAndMaxInTheirOwnSlots()
    {
        var reading = VcpBrightness.Interpret(true, VcpBrightness.SetParameter, current: 68, max: 100, win32Error: null);

        Assert.True(reading.Supported);
        Assert.Equal(0, reading.Min);
        Assert.Equal(100, reading.Max);
        Assert.Equal(68, reading.Current);
        Assert.Null(reading.Reason);
    }

    [Fact]
    public void Success_WithANonHundredMax_UsesTheMonitorsOwnScale()
    {
        var reading = VcpBrightness.Interpret(true, VcpBrightness.SetParameter, current: 20, max: 255, win32Error: null);

        Assert.True(reading.Supported);
        Assert.Equal(255, reading.Max);
        Assert.Equal(20, reading.Current);
    }

    [Fact]
    public void Success_WithAMomentaryTypeButAUsableRange_IsStillSupported()
    {
        // Some monitors report MC_MOMENTARY for a brightness control that works; the
        // value is trusted when the range is usable.
        var reading = VcpBrightness.Interpret(true, VcpBrightness.Momentary, current: 40, max: 100, win32Error: null);

        Assert.True(reading.Supported);
        Assert.Equal(40, reading.Current);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(5)]
    [InlineData(87)]
    [InlineData(1167)]
    public void Failure_ReportsAReasonThatNamesTheWin32Error(int error)
    {
        var reading = VcpBrightness.Interpret(false, VcpBrightness.SetParameter, current: 0, max: 0, win32Error: error);

        Assert.False(reading.Supported);
        Assert.False(string.IsNullOrWhiteSpace(reading.Reason));
        Assert.Contains(error.ToString(), reading.Reason);
    }

    [Fact]
    public void Failure_WithoutAWin32Error_StillReportsAReason()
    {
        var reading = VcpBrightness.Interpret(false, VcpBrightness.SetParameter, current: 0, max: 0, win32Error: null);

        Assert.False(reading.Supported);
        Assert.False(string.IsNullOrWhiteSpace(reading.Reason));
    }

    [Fact]
    public void MaxZero_IsUnsupported_WithAReason()
    {
        var reading = VcpBrightness.Interpret(true, VcpBrightness.SetParameter, current: 0, max: 0, win32Error: null);

        Assert.False(reading.Supported);
        Assert.Contains("maximum", reading.Reason);
    }

    [Fact]
    public void MaxZero_WithAMomentaryType_SaysTheControlIsMomentary()
    {
        var reading = VcpBrightness.Interpret(true, VcpBrightness.Momentary, current: 0, max: 0, win32Error: null);

        Assert.False(reading.Supported);
        Assert.Contains("momentary", reading.Reason);
    }

    [Fact]
    public void CurrentAboveMax_IsClampedToMax()
    {
        var reading = VcpBrightness.Interpret(true, VcpBrightness.SetParameter, current: 120, max: 100, win32Error: null);

        Assert.True(reading.Supported);
        Assert.Equal(100, reading.Max);
        Assert.Equal(100, reading.Current);
    }

    [Theory]
    [InlineData(int.MinValue, uint.MaxValue, int.MaxValue)]
    [InlineData(-5, 0, 0)]
    [InlineData(999, 1, 1)]
    [InlineData(0, 1, 0)]
    public void GarbageInput_NeverThrows(int codeType, uint current, uint max)
    {
        var reading = VcpBrightness.Interpret(true, codeType, current, max, win32Error: -1);

        Assert.NotNull(reading);
        Assert.True(reading.Current >= reading.Min);
        Assert.True(reading.Current <= reading.Max);
    }

    [Fact]
    public void GarbageInput_OnFailure_NeverThrows()
    {
        var reading = VcpBrightness.Interpret(false, int.MinValue, uint.MaxValue, uint.MaxValue, win32Error: int.MinValue);

        Assert.False(reading.Supported);
        Assert.False(string.IsNullOrWhiteSpace(reading.Reason));
    }

    [Fact]
    public void MaxAboveIntMaxValue_IsClampedIntoAnIntRange()
    {
        var reading = VcpBrightness.Interpret(true, VcpBrightness.SetParameter, current: uint.MaxValue, max: uint.MaxValue, win32Error: null);

        Assert.True(reading.Supported);
        Assert.Equal(int.MaxValue, reading.Max);
        Assert.Equal(int.MaxValue, reading.Current);
    }
}
