using System.Management;
using System.Runtime.Versioning;

namespace Pomodim.Backends.Windows;

/// <summary>
/// WMI brightness for internal panels: <c>root\wmi</c>
/// <c>WmiMonitorBrightness</c> (current values) and
/// <c>WmiMonitorBrightnessMethods</c> (<c>WmiSetBrightness</c>). External DDC/CI
/// monitors are handled by the monitor API; this fallback covers laptop panels
/// that expose no VCP brightness. WMI reports percentages, so the range is
/// [0, 100].
/// </summary>
[SupportedOSPlatform("windows")]
public class WmiBrightnessFallback
{
    private const string Namespace = @"root\wmi";

    /// <summary>
    /// One internal panel as WMI sees it. <see cref="Id"/> is the value of the
    /// WMI instance's key property; the <c>WmiMonitorBrightness</c> and
    /// <c>WmiMonitorBrightnessMethods</c> instances of one panel share it.
    /// </summary>
    public sealed record Panel(string Id, int Current, int Min, int Max);

    /// <summary>
    /// Every panel WMI reports a brightness for, in WMI order. The backend
    /// matches them to DDC/CI-unsupported monitors by ordinal position.
    /// </summary>
    public virtual IReadOnlyList<Panel> GetPanels()
    {
        var panels = new List<Panel>();
        var scope = new ManagementScope(Namespace);
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightness"));
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                var id = KeyValue(mo);
                if (id is null)
                    continue;
                var current = (int)mo["CurrentBrightness"];
                panels.Add(new Panel(id, current, 0, 100));
            }
        }
        return panels;
    }

    /// <summary>
    /// Sets the brightness of the panel whose key is <paramref name="id"/> via
    /// <c>WmiMonitorBrightnessMethods.WmiSetBrightness</c>.
    /// </summary>
    public virtual void SetBrightness(string id, int value)
    {
        var scope = new ManagementScope(Namespace);
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                if (KeyValue(mo) != id)
                    continue;
                mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)value });
                return;
            }
        }
        throw new KeyNotFoundException($"No WmiMonitorBrightnessMethods instance for panel '{id}'.");
    }

    /// <summary>The instance key of a WMI object, or null if it has no key.</summary>
    private static string? KeyValue(ManagementBaseObject mo)
    {
        var keys = mo.Properties.Cast<PropertyData>()
            .Where(p => p.Qualifiers["key"] is not null)
            .Select(p => p.Value?.ToString())
            .Where(v => v is not null)
            .Cast<string>()
            .ToList();
        return keys.Count switch
        {
            0 => null,
            1 => keys[0],
            _ => string.Join("|", keys),
        };
    }
}