namespace Pomodim.Core.Brightness;

/// <summary>
/// One display as a backend sees it. <see cref="Current"/> is a percentage within
/// [<see cref="Min"/>, <see cref="Max"/>]; for unsupported displays it is 0 and
/// <see cref="Reason"/> says why the backend cannot control it.
/// </summary>
public sealed record DisplayInfo(
    string Id,
    string Name,
    string BackendName,
    int Min,
    int Max,
    int Current,
    bool Supported,
    string? Reason);