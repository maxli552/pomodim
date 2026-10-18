using System.Runtime.Versioning;
using Pomodim.Core.Brightness;

namespace Pomodim.Backends.Windows;

/// <summary>
/// The Windows brightness backend. External monitors are controlled over DDC/CI
/// (VCP 0x10) through <see cref="IMonitorApi"/>; monitors without DDC/CI brightness
/// (typically the internal laptop panel) fall back to <see cref="IWmiBrightness"/>.
///
/// WMI panels are matched to the DDC/CI-unsupported monitors by ordinal position
/// <em>among the unsupported ones</em> — an external DDC display listed first must not
/// consume the laptop panel's WMI entry. Enumeration failures never escape: the backend
/// reports unsupported displays with a reason instead.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrightnessBackend : IBrightnessBackend, IDisposable
{
    private const string DdcName = "windows-ddc";
    private const string WmiName = "windows-wmi";
    private const string EnumerationErrorId = "windows-error";

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
    private readonly IWmiBrightness? _wmi;
    private readonly List<Display> _displays = [];
    private bool _disposed;

    internal WindowsBrightnessBackend(IMonitorApi monitors, IWmiBrightness? wmi = null)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _wmi = wmi;
    }

    /// <summary>The production backend: real DDC/CI plus the WMI fallback.</summary>
    public static WindowsBrightnessBackend CreateDefault() =>
        new(new WindowsMonitorApi(), new WmiBrightnessFallback());

    public string Name => "windows";

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Refresh();
        return _displays
            .Select(d => new DisplayInfo(d.Id, d.Name, d.BackendName, d.Min, d.Max, d.Current, d.Supported, d.Reason))
            .ToList();
    }

    public void SetBrightness(string id, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_displays.Count == 0)
            Refresh();
        return new(Name, _displays.ToDictionary(d => d.Id, d => d.Current));
    }

    public void Restore(BrightnessSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    /// <summary>Releases the physical-monitor handles the DDC/CI seam holds.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _monitors.Dispose();
    }

    private void Refresh()
    {
        _displays.Clear();

        IReadOnlyList<PhysicalMonitor> monitors;
        try
        {
            monitors = _monitors.GetDisplays();
        }
        catch (Exception ex)
        {
            // Enumeration must never throw at the display list: --diag has to report
            // something readable, and a tray app must not die on a monitor unplug.
            _displays.Add(new Display
            {
                Id = EnumerationErrorId,
                Name = "display enumeration",
                BackendName = Name,
                Min = 0,
                Max = 100,
                Current = 0,
                Supported = false,
                Reason = $"display enumeration failed: {ex.Message}",
            });
            return;
        }

        var panels = GetWmiPanels(out var wmiError);
        var panelIndex = 0;
        foreach (var monitor in monitors)
        {
            if (monitor.DdcSupported)
            {
                _displays.Add(new Display
                {
                    Id = monitor.Id,
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

            // No DDC/CI brightness: this monitor is a candidate for the internal-panel
            // fallback. Match only against the other unsupported monitors, so a DDC
            // display earlier in the list never consumes a WMI panel.
            if (panelIndex < panels.Count)
            {
                var panel = panels[panelIndex++];
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
                continue;
            }

            _displays.Add(new Display
            {
                Id = monitor.Id,
                Name = monitor.Name,
                BackendName = Name,
                Min = monitor.Min,
                Max = monitor.Max,
                Current = 0,
                Supported = false,
                Reason = UnsupportedReason(monitor.Reason, _wmi is null ? null : wmiError ?? "no WMI brightness panel available for this monitor"),
                Handle = monitor.Handle,
            });
        }
    }

    /// <summary>
    /// WMI panels, or an empty list plus the failure text. A broken WMI provider must not
    /// hide the DDC displays, so the exception is captured, not propagated.
    /// </summary>
    private IReadOnlyList<WmiBrightnessPanel> GetWmiPanels(out string? error)
    {
        error = null;
        if (_wmi is null)
            return [];
        try
        {
            return _wmi.GetPanels();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return [];
        }
    }

    /// <summary>
    /// The reason a monitor cannot be controlled: the DDC reason is kept (it is the most
    /// specific fact) and the WMI outcome is appended.
    /// </summary>
    private static string UnsupportedReason(string? ddcReason, string? wmiOutcome)
    {
        var reason = string.IsNullOrWhiteSpace(ddcReason)
            ? "no DDC/CI brightness"
            : ddcReason;
        return wmiOutcome is null ? reason : $"{reason}; WMI brightness unavailable: {wmiOutcome}";
    }
}
