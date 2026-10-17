using System.Runtime.Versioning;
using Pomodim.Core.Brightness;

namespace Pomodim.Backends.Windows;

/// <summary>
/// The Windows brightness backend. External monitors are controlled over DDC/CI
/// (VCP 0x10) through <see cref="IMonitorApi"/>; monitors without DDC/CI
/// brightness (typically the internal laptop panel) fall back to
/// <see cref="WmiBrightnessFallback"/>. The display list comes from the monitor
/// API; WMI panels are matched to DDC/CI-unsupported monitors by ordinal
/// position.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrightnessBackend : IBrightnessBackend
{
    private const string DdcName = "windows-ddc";
    private const string WmiName = "windows-wmi";

    private sealed class Display
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string BackendName { get; init; }
        public required int Min { get; init; }
        public required int Max { get; init; }
        public int Current { get; set; }
        public required bool Supported { get; init; }
        public required string? Reason { get; init; }
        public IntPtr Handle { get; init; }
        public string? WmiPanelId { get; init; }
    }

    private readonly IMonitorApi _monitors;
    private readonly WmiBrightnessFallback? _wmi;
    private readonly List<Display> _displays = [];

    internal WindowsBrightnessBackend(IMonitorApi monitors, WmiBrightnessFallback? wmi = null)
    {
        _monitors = monitors;
        _wmi = wmi;
    }

    /// <summary>The production backend: real DDC/CI plus the WMI fallback.</summary>
    public static WindowsBrightnessBackend CreateDefault() =>
        new(new WindowsMonitorApi(), new WmiBrightnessFallback());

    public string Name => "windows";

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        Refresh();
        return _displays
            .Select(d => new DisplayInfo(d.Id, d.Name, d.BackendName, d.Min, d.Max, d.Current, d.Supported, d.Reason))
            .ToList();
    }

    public void SetBrightness(string id, int value)
    {
        if (_displays.Count == 0)
            Refresh();
        var display = _displays.FirstOrDefault(d => d.Id == id)
            ?? throw new KeyNotFoundException($"Unknown display id '{id}'.");
        if (!display.Supported)
            throw new NotSupportedException($"Display '{display.Name}' is not supported: {display.Reason}");

        var applied = Math.Clamp(value, display.Min, display.Max);
        if (display.BackendName == DdcName)
            _monitors.SetVcpBrightness(display.Handle, applied);
        else
            _wmi!.SetBrightness(display.WmiPanelId!, applied);
        display.Current = applied;
    }

    public BrightnessSnapshot Snapshot()
    {
        if (_displays.Count == 0)
            Refresh();
        return new(Name, _displays.ToDictionary(d => d.Id, d => d.Current));
    }

    public void Restore(BrightnessSnapshot snapshot)
    {
        if (snapshot.BackendName != Name)
            throw new ArgumentException(
                $"Snapshot was taken from backend '{snapshot.BackendName}', not '{Name}'.",
                nameof(snapshot));
        if (_displays.Count == 0)
            Refresh();
        foreach (var (id, value) in snapshot.Values)
        {
            var display = _displays.FirstOrDefault(d => d.Id == id)
                ?? throw new KeyNotFoundException($"Unknown display id '{id}'.");
            if (!display.Supported)
                continue; // an unsupported display has no state to restore
            SetBrightness(id, value);
        }
    }

    private void Refresh()
    {
        _displays.Clear();
        var monitors = _monitors.GetDisplays();
        var panels = _wmi?.GetPanels() ?? [];
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            if (monitor.DdcSupported)
            {
                _displays.Add(new Display
                {
                    Id = DdcId(monitor.Handle),
                    Name = monitor.Name,
                    BackendName = DdcName,
                    Min = monitor.Min,
                    Max = monitor.Max,
                    Current = monitor.Current,
                    Supported = true,
                    Reason = null,
                    Handle = monitor.Handle,
                });
                continue;
            }

            // No DDC/CI brightness: the internal-panel fallback, matched by ordinal.
            var panel = i < panels.Count ? panels[i] : null;
            if (panel is not null)
            {
                _displays.Add(new Display
                {
                    Id = $"wmi-{panel.Id}",
                    Name = monitor.Name,
                    BackendName = WmiName,
                    Min = panel.Min,
                    Max = panel.Max,
                    Current = panel.Current,
                    Supported = true,
                    Reason = null,
                    WmiPanelId = panel.Id,
                });
            }
            else
            {
                _displays.Add(new Display
                {
                    Id = DdcId(monitor.Handle),
                    Name = monitor.Name,
                    BackendName = Name,
                    Min = monitor.Min,
                    Max = monitor.Max,
                    Current = 0,
                    Supported = false,
                    Reason = _wmi is null
                        ? monitor.Reason
                        : "no DDC/CI brightness (VCP 0x10) and no WMI brightness method",
                    Handle = monitor.Handle,
                });
            }
        }
    }

    private static string DdcId(IntPtr handle) => $"ddc-{handle.ToInt64():x}";
}