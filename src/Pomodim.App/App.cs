using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pomodim.App.Services;
using Pomodim.Core;
using Pomodim.Core.Break;
using Pomodim.Core.Brightness;

namespace Pomodim.App;

/// <summary>
/// The tray application. Owns the tray icon, the flyout window, the break engine and the
/// one timer that drives everything. No main window: the app lives in the tray and the
/// flyout is shown on demand.
/// </summary>
public sealed class App : Application
{
    private AppSettings _settings = new();
    private IBrightnessBackend _backend = null!;
    private BreakEngine _engine = null!;
    private DispatcherTimer? _tick;
    private TrayIcon? _tray;
    private FlyoutWindow? _flyout;
    private BrightnessSnapshot? _dimSnapshot;
    private bool _wasDimmed;
    private DateTimeOffset _lastTrayUpdate = DateTimeOffset.MinValue;

    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = AppSettings.Load();
        _backend = BackendFactory.Create();
        _engine = new BreakEngine(BuildProtocol(), _settings.DimDepth, new SystemClock());

        BuildTray();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        base.OnFrameworkInitializationCompleted();
    }

    private BreakProtocol BuildProtocol()
    {
        if (_settings.UseCustomProtocol)
        {
            return Protocols.Custom(
                TimeSpan.FromMinutes(Math.Max(1, _settings.WorkMinutes)),
                TimeSpan.FromSeconds(Math.Max(5, _settings.BreakSeconds)));
        }

        return _settings.ProtocolName switch
        {
            "20/5" => Protocols.TwentyFive,
            "45/5" => Protocols.FortyFiveFive,
            _ => Protocols.TwentyTwentyTwenty,
        };
    }

    private void BuildTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
        var icon = File.Exists(iconPath) ? new WindowIcon(iconPath) : null;

        var menu = new NativeMenu();
        var open = new NativeMenuItem("Open Pomodim");
        open.Click += (_, _) => ShowFlyout();
        var breakNow = new NativeMenuItem("Break now");
        breakNow.Click += (_, _) => StartBreakNow();
        var lockItem = new NativeMenuItem("Lock (never dim)");
        lockItem.Click += (_, _) => ToggleLock();
        menu.Items.Add(open);
        menu.Items.Add(breakNow);
        menu.Items.Add(lockItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Pomodim",
            IsVisible = true,
            Menu = menu,
        };
        _tray.Clicked += (_, _) => ShowFlyout();

        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private void OnTick()
    {
        _engine.Tick();

        if (_engine.IsDimmed != _wasDimmed)
        {
            if (_engine.IsDimmed)
            {
                ApplyDim();
            }
            else
            {
                RemoveDim();
            }

            _wasDimmed = _engine.IsDimmed;
        }

        // The tray tooltip is the only place a timer is ever shown, and only when asked.
        if (_settings.ShowTimer && DateTimeOffset.UtcNow - _lastTrayUpdate > TimeSpan.FromSeconds(1) && _tray is not null)
        {
            var remaining = _engine.PhaseRemaining;
            var label = _engine.Phase == BreakPhase.Break ? "break" : "work";
            _tray.ToolTipText = $"Pomodim — {label} {remaining:mm\\:ss}";
            _lastTrayUpdate = DateTimeOffset.UtcNow;
        }
        else if (!_settings.ShowTimer && _tray is not null && _tray.ToolTipText != "Pomodim")
        {
            _tray.ToolTipText = "Pomodim";
        }

        _flyout?.Refresh(_engine, _settings);
    }

    private void ApplyDim()
    {
        if (_settings.NeverDimWhileLocked && IsSessionLocked())
        {
            // Locked: skip this break's dim entirely rather than flipping the screen dark
            // while nobody is looking (and then back, mid-lock).
            return;
        }

        try
        {
            _dimSnapshot = _backend.Snapshot();
            var displays = _backend.GetDisplays();
            var depth = Math.Max(1, _settings.DimDepth);
            foreach (var display in displays.Where(d => d.Supported))
            {
                _backend.SetBrightness(
                    display.Id,
                    display.Min + (int)Math.Round((display.Max - display.Min) * depth / 100.0));
            }
        }
        catch
        {
            // A backend failure must not end the break loop.
        }
    }

    private void RemoveDim()
    {
        try
        {
            if (_dimSnapshot is not null)
            {
                _backend.Restore(_dimSnapshot);
                _dimSnapshot = null;
            }
        }
        catch
        {
            // Same reasoning: never let restore failures crash the tray app.
        }
    }

    // Windows exposes this through the input desktop; on other platforms the lock is
    // tracked manually from the tray menu.
    private bool _manualLock;

    private bool IsSessionLocked() => _manualLock;

    private void ToggleLock()
    {
        _manualLock = !_manualLock;
        if (_manualLock)
        {
            _engine.Lock();
        }
        else
        {
            _engine.Unlock();
        }
    }

    public void StartBreakNow()
    {
        _engine.ForceBreak();
        _wasDimmed = false;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _settings.Save();

        if (_settings.StartWithWindows && !Autostart.IsEnabled())
        {
            Autostart.Set(true);
        }
        else if (!_settings.StartWithWindows && Autostart.IsEnabled())
        {
            Autostart.Set(false);
        }

        var state = _engine.State;
        var phase = _engine.Phase;
        _engine = new BreakEngine(BuildProtocol(), _settings.DimDepth, new SystemClock());
        if (state == SessionState.Paused)
        {
            _engine.Pause();
        }

        if (phase == BreakPhase.Break)
        {
            _engine.ForceBreak();
        }
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        try
        {
            return _backend.GetDisplays();
        }
        catch
        {
            return Array.Empty<DisplayInfo>();
        }
    }

    public void SetAllBrightness(int value)
    {
        try
        {
            foreach (var display in _backend.GetDisplays().Where(d => d.Supported))
            {
                _backend.SetBrightness(display.Id, value);
            }
        }
        catch
        {
            // Best effort while the slider is being dragged.
        }
    }

    public void ResetTrayText()
    {
        if (_tray is not null)
        {
            _tray.ToolTipText = "Pomodim";
        }
    }

    private void ShowFlyout()
    {
        _flyout ??= new FlyoutWindow(this);
        _flyout.Refresh(_engine, _settings);

        if (!_flyout.IsVisible)
        {
            _flyout.Show();
            _flyout.PlaceNearTray();
        }
        else
        {
            _flyout.Activate();
        }
    }

    private void Quit()
    {
        RemoveDim();
        _tick?.Stop();
        if (_tray is not null)
        {
            _tray.IsVisible = false;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
