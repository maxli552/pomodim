namespace Pomodim.Core.Tests;

public class ScaffoldTests
{
    [Fact]
    public void CoreAssemblyExposesVersion()
    {
        Assert.Equal("0.1.0", Pomodim.Version);
    }
}