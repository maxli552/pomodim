using Pomodim.Core.Brightness;

namespace Pomodim.Cli.Tests;

/// <summary>
/// End-to-end tests driving <see cref="DimCli.Run"/> with the mock backends:
/// output lines and exit codes 0 / 2 / 3 (TASKS task 6, slice gate).
///
/// All tests live in one class because they redirect the process-wide Console;
/// xunit runs the tests of one class sequentially, so the redirection is safe.
/// </summary>
public class DimCliTests : IDisposable
{
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;

    public DimCliTests()
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
    }

    private (int Exit, string Stdout, string Stderr) Run(
        string[] args,
        IReadOnlyList<IBrightnessBackend>? backends = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        var exit = backends is null ? DimCli.Run(args) : DimCli.Run(args, backends);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static string[] Lines(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

    /// <summary>
    /// The mock backends, injected explicitly so the diag output tests are
    /// deterministic on every OS (on Windows the default registry also contains
    /// the Windows backend, which is the default there).
    /// </summary>
    private static IReadOnlyList<IBrightnessBackend> MockBackends() =>
    [
        new MockBrightnessBackend(),
        new MockBrightnessBackend(MockBrightnessBackend.UnsupportedOnlyDisplays(), name: "mock-unsupported"),
    ];

    [Fact]
    public void Diag_MockBackends_ListEveryMockDisplay_AndExits0()
    {
        var (exit, stdout, _) = Run(["--diag"], MockBackends());

        Assert.Equal(DimCli.ExitOk, exit);
        string[] expected =
        [
            "Mock Display 1 | backend=mock | 80/0/100",
            "Mock Display 2 | backend=mock | 50/10/90",
            "Mock Display 3 | backend=mock | unsupported: no backlight control (mock display without DDC/CI)",
        ];
        Assert.Equal(expected, Lines(stdout));
    }

    [Fact]
    public void Diag_DefaultBackend_IsWindowsOnWindows_AndMockElsewhere()
    {
        var (exit, stdout, _) = Run(["--diag"]);
        if (OperatingSystem.IsWindows())
        {
            // The Windows backend is the default; a VM may have no displays (exit 3).
            Assert.DoesNotContain("backend=mock", stdout);
        }
        else
        {
            Assert.Equal(DimCli.ExitOk, exit);
            Assert.Contains("backend=mock", stdout);
        }
    }

    [Fact]
    public void Diag_MockBackend_Exits0_WithOneLinePerDisplay()
    {
        var (exit, stdout, _) = Run(["--diag", "--backend", "mock"]);

        Assert.Equal(DimCli.ExitOk, exit);
        var lines = Lines(stdout);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Mock Display 1 | backend=mock | 80/0/100", lines[0]);
        Assert.Equal("Mock Display 2 | backend=mock | 50/10/90", lines[1]);
        Assert.Equal(
            "Mock Display 3 | backend=mock | unsupported: no backlight control (mock display without DDC/CI)",
            lines[2]);
    }

    [Fact]
    public void Diag_MockUnsupported_Exits3_BecauseNothingIsControllable()
    {
        var (exit, stdout, _) = Run(["--diag", "--backend", "mock-unsupported"]);

        Assert.Equal(DimCli.ExitNoControl, exit);
        string[] expected =
        [
            "Mock Display (unsupported) | backend=mock-unsupported | unsupported: no backlight control (mock display without DDC/CI)",
        ];
        Assert.Equal(expected, Lines(stdout));
    }

    [Fact]
    public void Diag_ZeroDisplays_Exits3_AndDimsNothing()
    {
        var backend = new MockBrightnessBackend([]);
        var (exit, stdout, stderr) = Run(["--diag"], [backend]);

        Assert.Equal(DimCli.ExitNoControl, exit);
        Assert.Empty(Lines(stdout)); // no display lines: nothing was dimmed
        Assert.Contains("no displays found", stderr);
        Assert.Empty(backend.Calls); // no brightness write was issued
    }

    [Fact]
    public void NoAction_Exits2()
    {
        var (exit, _, stderr) = Run([]);

        Assert.Equal(DimCli.ExitUsage, exit);
        Assert.Contains("no action given", stderr);
    }

    [Fact]
    public void UnknownArgument_Exits2()
    {
        var (exit, _, stderr) = Run(["--bogus"]);

        Assert.Equal(DimCli.ExitUsage, exit);
        Assert.Contains("unknown argument '--bogus'", stderr);
    }

    [Fact]
    public void BackendWithoutName_Exits2()
    {
        var (exit, _, stderr) = Run(["--diag", "--backend"]);

        Assert.Equal(DimCli.ExitUsage, exit);
        Assert.Contains("--backend requires a name", stderr);
    }

    [Fact]
    public void UnknownBackend_Exits2()
    {
        var (exit, _, stderr) = Run(["--diag", "--backend", "nope"]);

        Assert.Equal(DimCli.ExitUsage, exit);
        Assert.Contains("unknown backend 'nope'", stderr);
    }

    [Fact]
    public void Help_Exits0()
    {
        var (exit, stdout, _) = Run(["--help"]);

        Assert.Equal(DimCli.ExitOk, exit);
        Assert.Contains("usage: dim --diag", stdout);
    }
}