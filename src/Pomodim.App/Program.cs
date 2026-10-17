using Avalonia;

namespace Pomodim.App;

internal static class Program
{
    // The tray app owns no main window, so the classic desktop lifetime is shut down
    // explicitly only when the tray menu says so.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
