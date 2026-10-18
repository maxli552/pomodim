namespace Pomodim.Core.Brightness;

/// <summary>
/// The result of reading one VCP feature (DDC/CI), already interpreted: whether the
/// feature is usable, the range the monitor reports, and — when it is not usable — a
/// human-readable <see cref="Reason"/>. Produced by <see cref="VcpBrightness.Interpret"/>
/// from the raw values a platform backend read, so the interpretation itself is pure
/// logic and testable on any OS.
/// </summary>
/// <param name="Supported">True when the feature exists and can be written.</param>
/// <param name="Min">
/// Lower bound of the range. DDC/CI reports no minimum, so this is always 0.
/// </param>
/// <param name="Max">Upper bound the monitor reports (0 when unknown/unsupported).</param>
/// <param name="Current">Current value, clamped into [Min, Max].</param>
/// <param name="Reason">Why the feature is not usable; null when supported.</param>
public sealed record VcpReading(
    bool Supported,
    int Min,
    int Max,
    int Current,
    string? Reason);

/// <summary>
/// Pure interpretation of a DDC/CI VCP brightness read. No P/Invoke, no OS types: the
/// platform layer (Dxva2 on Windows, IOKit on macOS, ...) does the raw call and passes
/// the result here. <c>Interpret</c> never throws, whatever it is given.
/// </summary>
public static class VcpBrightness
{
    /// <summary>The VCP code for brightness (continuous control).</summary>
    public const byte Code = 0x10;

    /// <summary>
    /// <c>MC_VCP_CODE_TYPE</c> = <c>MC_MOMENTARY</c>: the control returns to its previous
    /// state, so it cannot hold a brightness level.
    /// </summary>
    public const int Momentary = 0;

    /// <summary>
    /// <c>MC_VCP_CODE_TYPE</c> = <c>MC_SET_PARAMETER</c>: the control keeps the value it
    /// was given. This is what a brightness control normally reports.
    /// </summary>
    public const int SetParameter = 1;

    /// <summary>
    /// Turns a raw VCP brightness read into a <see cref="VcpReading"/>.
    /// </summary>
    /// <param name="succeeded">
    /// Whether the native read succeeded (a nonzero Win32 <c>BOOL</c>); the caller is
    /// responsible for the boolean decoding, this method only interprets it.
    /// </param>
    /// <param name="vcpCodeType">
    /// The <c>MC_VCP_CODE_TYPE</c> the monitor reported. A momentary type with a usable
    /// range is still accepted: some monitors report 0 for a working brightness control,
    /// and the value is trusted when the range is usable.
    /// </param>
    /// <param name="current">The current value, as read.</param>
    /// <param name="max">The maximum value, as read.</param>
    /// <param name="win32Error">The Win32 error from the failed call, if any.</param>
    public static VcpReading Interpret(
        bool succeeded,
        int vcpCodeType,
        uint current,
        uint max,
        int? win32Error)
    {
        if (!succeeded)
            return new VcpReading(false, 0, 100, 0, ReadFailure(win32Error));

        if (max == 0)
        {
            var reason = vcpCodeType == Momentary
                ? "the monitor reports VCP 0x10 as a momentary control with no brightness range"
                : "the monitor reports a maximum VCP brightness of 0 (no usable range)";
            return new VcpReading(false, 0, 0, 0, reason);
        }

        var maxValue = max > int.MaxValue ? int.MaxValue : (int)max;
        var currentValue = current > (uint)maxValue ? maxValue : (int)current;
        return new VcpReading(true, 0, maxValue, currentValue, null);
    }

    /// <summary>
    /// A readable description of a Win32 error code from a failed DDC/CI call. Only
    /// integers in, text out — no Windows types — so Core stays portable.
    /// </summary>
    public static string DescribeWin32Error(int code) => code switch
    {
        0 => "the monitor did not answer the DDC/CI request (Win32 error 0)",
        5 => "access to the monitor was denied (Win32 error 5)",
        6 => "the monitor handle is invalid (Win32 error 6)",
        31 => "the monitor did not answer the DDC/CI request (Win32 error 31)",
        87 => "the monitor or its driver rejected the VCP request (Win32 error 87)",
        1167 => "the monitor is not connected (Win32 error 1167)",
        1168 => "the monitor was not found (Win32 error 1168)",
        _ => $"the DDC/CI request failed (Win32 error {code})",
    };

    private static string ReadFailure(int? win32Error) =>
        win32Error is int code
            ? DescribeWin32Error(code)
            : "the DDC/CI brightness read failed without a Win32 error code";
}
