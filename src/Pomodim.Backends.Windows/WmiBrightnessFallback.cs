using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace Pomodim.Backends.Windows;

/// <summary>
/// One internal panel as WMI sees it. <see cref="Id"/> is the WMI instance key;
/// <c>WmiMonitorBrightness</c> and <c>WmiMonitorBrightnessMethods</c> instances of the
/// same panel share it, so the panel is matched by key, never by index.
/// <see cref="Min"/>/<see cref="Max"/> come from the panel's own <c>Levels</c> array.
/// </summary>
public sealed record WmiBrightnessPanel(string Id, int Current, int Min, int Max);

/// <summary>
/// The WMI fallback contract the Windows backend uses, so tests can substitute a fake.
/// Implementations throw (with a readable message) when WMI itself fails; the backend
/// turns that into a display <c>Reason</c> instead of letting it escape.
/// </summary>
internal interface IWmiBrightness
{
    /// <summary>Every panel WMI reports a brightness for, in WMI order.</summary>
    IReadOnlyList<WmiBrightnessPanel> GetPanels();

    /// <summary>Sets one panel's brightness through <c>WmiMonitorBrightnessMethods</c>.</summary>
    void SetBrightness(string id, int value);
}

/// <summary>One WMI object: the properties and method invocation the fallback needs.</summary>
internal interface IWmiObject : IDisposable
{
    /// <summary>The instance key (the WMI <c>key</c> properties joined with '|'), or null.</summary>
    string? Key { get; }

    /// <summary>A property value, boxed exactly as WMI returns it (byte stays byte).</summary>
    object? GetProperty(string name);

    /// <summary>Invokes a method and returns its return value, boxed.</summary>
    object? InvokeMethod(string method, object[] arguments);
}

/// <summary>A WMI class query. Throws when the query itself fails.</summary>
internal interface IWmiQuery
{
    IReadOnlyList<IWmiObject> Select(string className);
}

/// <summary>
/// WMI brightness for internal panels: <c>root\wmi</c> <c>WmiMonitorBrightness</c>
/// (current values and <c>Levels</c>) and <c>WmiMonitorBrightnessMethods</c>
/// (<c>WmiSetBrightness</c>). External DDC/CI monitors are handled by the monitor API;
/// this fallback covers laptop panels that expose no VCP brightness.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiBrightnessFallback : IWmiBrightness
{
    private const string BrightnessClass = "WmiMonitorBrightness";
    private const string MethodsClass = "WmiMonitorBrightnessMethods";
    private const string SetMethod = "WmiSetBrightness";

    private readonly IWmiQuery _query;

    /// <summary>The production fallback: real WMI via <see cref="ManagementWmiQuery"/>.</summary>
    public WmiBrightnessFallback()
        : this(new ManagementWmiQuery())
    {
    }

    internal WmiBrightnessFallback(IWmiQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    /// <summary>
    /// Every panel WMI reports a brightness for. <c>CurrentBrightness</c> is unboxed with
    /// <see cref="Convert.ToInt32(object, IFormatProvider)"/> because WMI hands back a
    /// <see cref="byte"/>, and a direct <c>(int)</c> cast would throw.
    /// </summary>
    public IReadOnlyList<WmiBrightnessPanel> GetPanels()
    {
        var panels = new List<WmiBrightnessPanel>();
        foreach (var instance in _query.Select(BrightnessClass))
        {
            using (instance)
            {
                if (instance.Key is not { Length: > 0 } id)
                    continue; // no instance key: cannot be addressed, cannot be matched
                var (min, max) = LevelsRange(instance.GetProperty("Levels"));
                var current = Convert.ToInt32(instance.GetProperty("CurrentBrightness"), CultureInfo.InvariantCulture);
                panels.Add(new WmiBrightnessPanel(id, Math.Clamp(current, min, max), min, max));
            }
        }
        return panels;
    }

    /// <summary>
    /// Sets the brightness of the panel whose key is <paramref name="id"/>.
    /// <c>WmiSetBrightness(Timeout, Brightness)</c> takes two arguments — a timeout of 0
    /// ("apply immediately") and a <see cref="byte"/> level — and returns a status where
    /// nonzero means failure.
    /// </summary>
    public void SetBrightness(string id, int value)
    {
        foreach (var instance in _query.Select(MethodsClass))
        {
            using (instance)
            {
                if (instance.Key != id)
                    continue;

                var level = (byte)Math.Clamp(value, 0, byte.MaxValue);
                var status = Convert.ToUInt32(
                    instance.InvokeMethod(SetMethod, [0u, level]) ?? 0u,
                    CultureInfo.InvariantCulture);
                // WMI method status: 0 = success, anything else = failure (this is a
                // WMI return code, not one of the Win32 BOOLs above).
                if (status > 0)
                    throw new IOException($"WmiSetBrightness failed for panel '{id}' (status {status}).");
                return;
            }
        }
        throw new KeyNotFoundException($"No WmiMonitorBrightnessMethods instance for panel '{id}'.");
    }

    /// <summary>
    /// The [min, max] range from a <c>Levels</c> array (the brightness values the panel
    /// actually supports). Missing or empty <c>Levels</c> falls back to the WMI
    /// percentage range [0, 100].
    /// </summary>
    private static (int Min, int Max) LevelsRange(object? levels)
    {
        var values = new List<int>();
        switch (levels)
        {
            case byte[] bytes:
                values.AddRange(bytes.Select(v => (int)v));
                break;
            case ushort[] ushorts:
                values.AddRange(ushorts.Select(v => (int)v));
                break;
            case int[] ints:
                values.AddRange(ints);
                break;
            case Array array:
                // Brightness levels are percentages (0-255), so int is wide enough.
                foreach (var item in array)
                    if (item is not null)
                        values.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
                break;
        }

        if (values.Count == 0)
            return (0, 100);
        return (values.Min(), values.Max());
    }
}

/// <summary>The real WMI query: <c>SELECT * FROM &lt;class&gt;</c> in <c>root\wmi</c>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ManagementWmiQuery : IWmiQuery
{
    private const string Namespace = @"root\wmi";

    public IReadOnlyList<IWmiObject> Select(string className)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WMI is only available on Windows.");

        // className is an internal constant from this file, never user input.
        var scope = new ManagementScope(Namespace);
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}"));
        var result = new List<IWmiObject>();
        foreach (ManagementObject instance in searcher.Get())
            result.Add(new ManagementWmiObject(instance));
        return result;
    }
}

/// <summary>A thin adapter over one <see cref="ManagementObject"/>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ManagementWmiObject : IWmiObject
{
    private readonly ManagementObject _instance;

    public ManagementWmiObject(ManagementObject instance)
    {
        _instance = instance;
        Key = KeyOf(instance);
    }

    public string? Key { get; }

    public object? GetProperty(string name) => _instance[name];

    public object? InvokeMethod(string method, object[] arguments) => _instance.InvokeMethod(method, arguments);

    public void Dispose() => _instance.Dispose();

    /// <summary>The instance key from the WMI <c>key</c> qualifiers, or null if it has none.</summary>
    private static string? KeyOf(ManagementBaseObject instance)
    {
        var keys = instance.Properties
            .Cast<PropertyData>()
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
