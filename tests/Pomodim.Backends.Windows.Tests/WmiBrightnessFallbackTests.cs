using Pomodim.Backends.Windows;

namespace Pomodim.Backends.Windows.Tests;

/// <summary>
/// Tests for the WMI fallback behind the <see cref="IWmiQuery"/>/<see cref="IWmiObject"/>
/// seams. They pin down the round-5 defects: the two-argument <c>WmiSetBrightness</c>
/// call, the byte-typed level, unboxing <c>CurrentBrightness</c> instead of casting it,
/// and deriving the range from <c>Levels</c>.
/// </summary>
public class WmiBrightnessFallbackTests
{
    private const string BrightnessClass = "WmiMonitorBrightness";
    private const string MethodsClass = "WmiMonitorBrightnessMethods";

    private static FakeWmiObject BrightnessInstance(
        string key,
        object? current,
        object? levels = null)
    {
        var instance = new FakeWmiObject { Key = key };
        instance.Properties["CurrentBrightness"] = current;
        if (levels is not null)
            instance.Properties["Levels"] = levels;
        return instance;
    }

    private static FakeWmiObject MethodsInstance(string key, uint status = 0)
    {
        var instance = new FakeWmiObject { Key = key };
        instance.ReturnValue = status;
        return instance;
    }

    [Fact]
    public void SetBrightness_CallsWmiSetBrightnessWithTimeoutZeroAndAByteLevel()
    {
        var query = new FakeWmiQuery();
        var method = query.Add(MethodsClass, MethodsInstance("panel-key"));
        var fallback = new WmiBrightnessFallback(query);

        fallback.SetBrightness("panel-key", 40);

        var invocation = Assert.Single(method.Invocations);
        Assert.Equal("WmiSetBrightness", invocation.Method);
        Assert.Equal(2, invocation.Arguments.Length);            // 2-argument overload
        Assert.Equal(0u, invocation.Arguments[0]);               // timeout 0 = apply now
        Assert.IsType<byte>(invocation.Arguments[1]);            // byte, not int/uint
        Assert.Equal((byte)40, invocation.Arguments[1]);
    }

    [Theory]
    [InlineData(300, 255)]
    [InlineData(-5, 0)]
    [InlineData(100, 100)]
    public void SetBrightness_ClampsTheLevelIntoByteRange(int requested, int expected)
    {
        var query = new FakeWmiQuery();
        var method = query.Add(MethodsClass, MethodsInstance("panel-key"));
        var fallback = new WmiBrightnessFallback(query);

        fallback.SetBrightness("panel-key", requested);

        Assert.Equal((byte)expected, Assert.Single(method.Invocations).Arguments[1]);
    }

    [Fact]
    public void SetBrightness_MatchesThePanelByInstanceKey_NotByIndex()
    {
        var query = new FakeWmiQuery();
        var first = query.Add(MethodsClass, MethodsInstance("panel-a"));
        var second = query.Add(MethodsClass, MethodsInstance("panel-b"));
        var fallback = new WmiBrightnessFallback(query);

        fallback.SetBrightness("panel-b", 55);

        Assert.Empty(first.Invocations);
        Assert.Equal((byte)55, Assert.Single(second.Invocations).Arguments[1]);
    }

    [Fact]
    public void SetBrightness_NonZeroStatus_ThrowsAReadableError()
    {
        var query = new FakeWmiQuery();
        query.Add(MethodsClass, MethodsInstance("panel-key", status: 1));
        var fallback = new WmiBrightnessFallback(query);

        var error = Assert.Throws<IOException>(() => fallback.SetBrightness("panel-key", 40));

        Assert.Contains("WmiSetBrightness", error.Message);
        Assert.Contains("status 1", error.Message);
    }

    [Fact]
    public void SetBrightness_UnknownKey_Throws()
    {
        var query = new FakeWmiQuery();
        query.Add(MethodsClass, MethodsInstance("panel-key"));
        var fallback = new WmiBrightnessFallback(query);

        Assert.Throws<KeyNotFoundException>(() => fallback.SetBrightness("nope", 40));
    }

    [Fact]
    public void GetPanels_UnboxesTheByteCurrentBrightness()
    {
        var query = new FakeWmiQuery();
        query.Add(BrightnessClass, BrightnessInstance("panel-key", (byte)40, new byte[] { 0, 10, 100 }));
        var fallback = new WmiBrightnessFallback(query);

        var panel = Assert.Single(fallback.GetPanels());

        Assert.Equal(40, panel.Current);
    }

    [Fact]
    public void GetPanels_DerivesMinAndMaxFromLevels()
    {
        var query = new FakeWmiQuery();
        query.Add(BrightnessClass, BrightnessInstance("panel-key", (byte)60, new byte[] { 10, 30, 100 }));
        var fallback = new WmiBrightnessFallback(query);

        var panel = Assert.Single(fallback.GetPanels());

        Assert.Equal(10, panel.Min);
        Assert.Equal(100, panel.Max);
    }

    [Fact]
    public void GetPanels_ClampsTheCurrentValueIntoTheLevelsRange()
    {
        var query = new FakeWmiQuery();
        query.Add(BrightnessClass, BrightnessInstance("panel-key", (byte)5, new byte[] { 20, 50, 90 }));
        var fallback = new WmiBrightnessFallback(query);

        var panel = Assert.Single(fallback.GetPanels());

        Assert.Equal(20, panel.Min);
        Assert.Equal(20, panel.Current);
    }

    [Fact]
    public void GetPanels_WithoutLevels_UsesThePercentRange()
    {
        var query = new FakeWmiQuery();
        query.Add(BrightnessClass, BrightnessInstance("panel-key", (byte)40));
        var fallback = new WmiBrightnessFallback(query);

        var panel = Assert.Single(fallback.GetPanels());

        Assert.Equal(0, panel.Min);
        Assert.Equal(100, panel.Max);
    }

    [Fact]
    public void GetPanels_KeepsTheInstanceKeyAsThePanelId()
    {
        var query = new FakeWmiQuery();
        query.Add(BrightnessClass, BrightnessInstance(@"DISPLAY\AUO1234\5&1a2b", (byte)40));
        var fallback = new WmiBrightnessFallback(query);

        Assert.Equal(@"DISPLAY\AUO1234\5&1a2b", Assert.Single(fallback.GetPanels()).Id);
    }

    [Fact]
    public void GetPanels_SkipsInstancesWithoutAKey()
    {
        var query = new FakeWmiQuery();
        query.Add(BrightnessClass, BrightnessInstance(string.Empty, (byte)40));
        query.Add(BrightnessClass, BrightnessInstance(null!, (byte)40));
        var fallback = new WmiBrightnessFallback(query);

        Assert.Empty(fallback.GetPanels());
    }

    [Fact]
    public void GetPanels_LetAQueryFailureEscape_SoTheBackendCanTurnItIntoAReason()
    {
        var query = new FakeWmiQuery { Failure = new InvalidOperationException("WMI provider unavailable") };
        var fallback = new WmiBrightnessFallback(query);

        var error = Assert.Throws<InvalidOperationException>(() => fallback.GetPanels());

        Assert.Contains("WMI provider unavailable", error.Message);
    }

    [Fact]
    public void GetPanels_ClosesEveryInstanceItOpened()
    {
        var query = new FakeWmiQuery();
        var instance = query.Add(BrightnessClass, BrightnessInstance("panel-key", (byte)40));
        var fallback = new WmiBrightnessFallback(query);

        _ = fallback.GetPanels();

        Assert.True(instance.Disposed);
    }
}
