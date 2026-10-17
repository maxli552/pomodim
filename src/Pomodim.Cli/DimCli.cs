using System.Globalization;
using Pomodim.Core.Brightness;

namespace Pomodim.Cli;

/// <summary>
/// Argument parsing and actions for the <c>dim</c> CLI.
///
/// Exit codes: 0 = ok (at least one controllable display), 2 = usage error,
/// 3 = no controllable display (nothing to dim).
/// </summary>
public static class DimCli
{
    public const int ExitOk = 0;
    public const int ExitUsage = 2;
    public const int ExitNoControl = 3;

    public static int Run(string[] args) => Run(args, BackendRegistry.CreateDefault());

    /// <summary>
    /// Runs the CLI against an explicit backend list. The default list comes from
    /// <see cref="BackendRegistry"/>; tests inject their own (e.g. a mock with only
    /// unsupported displays) to exercise every exit code.
    /// </summary>
    public static int Run(string[] args, IReadOnlyList<IBrightnessBackend> backends)
    {
        var diag = false;
        string? backendName = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--diag":
                    diag = true;
                    break;
                case "--backend":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --backend requires a name");
                        return ExitUsage;
                    }
                    backendName = args[++i];
                    break;
                case "--help":
                case "-h":
                    PrintUsage(Console.Out);
                    return ExitOk;
                default:
                    Console.Error.WriteLine($"error: unknown argument '{args[i]}'");
                    PrintUsage(Console.Error);
                    return ExitUsage;
            }
        }

        if (!diag)
        {
            Console.Error.WriteLine("error: no action given");
            PrintUsage(Console.Error);
            return ExitUsage;
        }

        var selected = SelectBackend(backends, backendName, out var error);
        if (selected is null)
        {
            Console.Error.WriteLine(error);
            return ExitUsage;
        }

        return Diag(selected);
    }

    private static IBrightnessBackend? SelectBackend(
        IReadOnlyList<IBrightnessBackend> backends,
        string? name,
        out string error)
    {
        error = string.Empty;
        if (backends.Count == 0)
        {
            error = "no brightness backends are available on this system";
            return null;
        }

        if (name is not null)
        {
            var match = backends.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                error = $"unknown backend '{name}' (available: {string.Join(", ", backends.Select(b => b.Name))})";
                return null;
            }
            return match;
        }

        // No explicit name: on Windows the default is the Windows backend (it is
        // registered only on Windows); elsewhere the first backend that can
        // control at least one display, else the first backend so its unsupported
        // displays are still listed.
        if (OperatingSystem.IsWindows())
        {
            var windows = backends.FirstOrDefault(b => b.Name == "windows");
            if (windows is not null)
                return windows;
        }
        return backends.FirstOrDefault(b => b.GetDisplays().Any(d => d.Supported)) ?? backends[0];
    }

    private static int Diag(IBrightnessBackend backend)
    {
        var displays = backend.GetDisplays();
        if (displays.Count == 0)
        {
            Console.Error.WriteLine($"backend '{backend.Name}': no displays found");
            return ExitNoControl;
        }

        foreach (var d in displays)
        {
            if (d.Supported)
                Console.WriteLine(
                    string.Create(CultureInfo.InvariantCulture, $"{d.Name} | backend={d.BackendName} | {d.Current}/{d.Min}/{d.Max}"));
            else
                Console.WriteLine($"{d.Name} | backend={d.BackendName} | unsupported: {d.Reason}");
        }

        return displays.Any(d => d.Supported) ? ExitOk : ExitNoControl;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("usage: dim --diag [--backend <name>]");
        writer.WriteLine();
        writer.WriteLine("  --diag            list every display with its backend and brightness range");
        writer.WriteLine("  --backend <name>  select a backend explicitly (default: first supported)");
        writer.WriteLine();
        writer.WriteLine("exit codes: 0 = at least one controllable display, 2 = usage error, 3 = no controllable display");
    }
}