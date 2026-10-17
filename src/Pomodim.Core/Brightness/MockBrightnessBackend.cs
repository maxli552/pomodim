namespace Pomodim.Core.Brightness;

/// <summary>One display in the mock backend.</summary>
public sealed record MockDisplaySpec(
    string Id,
    string Name,
    int Min,
    int Max,
    int Initial,
    bool Supported = true,
    string? Reason = null);

/// <summary>
/// A recorded brightness write: the display id, the requested value, and the value
/// actually applied after clamping.
/// </summary>
public sealed record SetCall(string Id, int Requested, int Applied);

/// <summary>
/// In-memory backend with a fixed, deterministic set of displays. Used by tests and by
/// CI/VM runs where no real display exists. The default set always contains at least one
/// unsupported display so the "unsupported: reason" path is exercised.
/// </summary>
public sealed class MockBrightnessBackend : IBrightnessBackend
{
    private sealed class Display
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required int Min { get; init; }
        public required int Max { get; init; }
        public int Current { get; set; }
        public required bool Supported { get; init; }
        public required string? Reason { get; init; }
    }

    private readonly List<Display> _displays;
    private readonly List<SetCall> _calls = [];

    public MockBrightnessBackend()
        : this(DefaultDisplays())
    {
    }

    public MockBrightnessBackend(IEnumerable<MockDisplaySpec> displays)
    {
        _displays = displays
            .Select(d => new Display
            {
                Id = d.Id,
                Name = d.Name,
                Min = d.Min,
                Max = d.Max,
                Current = Math.Clamp(d.Initial, d.Min, d.Max),
                Supported = d.Supported,
                Reason = d.Reason,
            })
            .ToList();
    }

    public string Name => "mock";

    /// <summary>Every brightness write since construction, in order.</summary>
    public IReadOnlyList<SetCall> Calls => _calls.AsReadOnly();

    /// <summary>The deterministic default display set.</summary>
    public static IReadOnlyList<MockDisplaySpec> DefaultDisplays() =>
    [
        new MockDisplaySpec("mock-1", "Mock Display 1", 0, 100, 80),
        new MockDisplaySpec("mock-2", "Mock Display 2", 10, 90, 50),
        new MockDisplaySpec(
            "mock-3",
            "Mock Display 3",
            0,
            100,
            0,
            Supported: false,
            Reason: "no backlight control (mock display without DDC/CI)"),
    ];

    public IReadOnlyList<DisplayInfo> GetDisplays() =>
        _displays
            .Select(d => new DisplayInfo(d.Id, d.Name, Name, d.Min, d.Max, d.Current, d.Supported, d.Reason))
            .ToList();

    public void SetBrightness(string id, int value)
    {
        var display = _displays.FirstOrDefault(d => d.Id == id)
            ?? throw new KeyNotFoundException($"Unknown display id '{id}'.");
        if (!display.Supported)
            throw new NotSupportedException($"Display '{display.Name}' is not supported: {display.Reason}");
        var applied = Math.Clamp(value, display.Min, display.Max);
        display.Current = applied;
        _calls.Add(new SetCall(id, value, applied));
    }

    public BrightnessSnapshot Snapshot() =>
        new(Name, _displays.ToDictionary(d => d.Id, d => d.Current));

    public void Restore(BrightnessSnapshot snapshot)
    {
        if (snapshot.BackendName != Name)
            throw new ArgumentException(
                $"Snapshot was taken from backend '{snapshot.BackendName}', not '{Name}'.",
                nameof(snapshot));
        foreach (var (id, value) in snapshot.Values)
        {
            var display = _displays.FirstOrDefault(d => d.Id == id)
                ?? throw new KeyNotFoundException($"Unknown display id '{id}'.");
            if (!display.Supported)
                continue; // an unsupported display has no state to restore
            SetBrightness(id, value);
        }
    }
}