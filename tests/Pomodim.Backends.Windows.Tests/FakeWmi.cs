using Pomodim.Backends.Windows;

namespace Pomodim.Backends.Windows.Tests;

/// <summary>A fake WMI object: records invocations and returns a canned value.</summary>
internal sealed class FakeWmiObject : IWmiObject
{
    public string? Key { get; init; }

    public Dictionary<string, object?> Properties { get; } = [];

    public List<(string Method, object?[] Arguments)> Invocations { get; } = [];

    public object? ReturnValue { get; set; }

    public bool Disposed { get; private set; }

    public object? GetProperty(string name) => Properties.GetValueOrDefault(name);

    public object? InvokeMethod(string method, object[] arguments)
    {
        Invocations.Add((method, arguments));
        return ReturnValue;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>A fake <c>root\wmi</c> query: canned objects per class, or one failure.</summary>
internal sealed class FakeWmiQuery : IWmiQuery
{
    public Dictionary<string, List<IWmiObject>> Classes { get; } = [];

    /// <summary>When set, every query throws it (a broken WMI provider).</summary>
    public Exception? Failure { get; set; }

    public FakeWmiObject Add(string className, FakeWmiObject instance)
    {
        if (!Classes.TryGetValue(className, out var list))
            Classes[className] = list = [];
        list.Add(instance);
        return instance;
    }

    public IReadOnlyList<IWmiObject> Select(string className)
    {
        if (Failure is not null)
            throw Failure;
        return Classes.GetValueOrDefault(className) ?? [];
    }
}
