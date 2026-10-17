using System.Text.Json;

namespace Pomodim.App.Services;

/// <summary>
/// Everything the person can change, persisted next to the user's app data.
/// Protocol is stored as a preset name plus explicit minutes so a custom protocol
/// survives a restart unchanged.
/// </summary>
public sealed class AppSettings
{
    public string ProtocolName { get; set; } = "20/20/20";
    public int WorkMinutes { get; set; } = 22;
    public int BreakSeconds { get; set; } = 20;
    public int DimDepth { get; set; } = 30;
    public bool ShowTimer { get; set; }
    public bool StartWithWindows { get; set; }
    public bool NeverDimWhileLocked { get; set; } = true;
    public bool UseCustomProtocol { get; set; }

    private static string Dir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pomodim");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // A corrupt settings file must never stop the tray app from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Persisting is best effort: a read-only profile must not crash the app.
        }
    }
}
