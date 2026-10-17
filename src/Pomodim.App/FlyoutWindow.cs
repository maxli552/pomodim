using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pomodim.App.Services;
using Pomodim.Core;
using Pomodim.Core.Break;

namespace Pomodim.App;

/// <summary>
/// The flyout: a borderless, topmost window with the brightness slider, the break protocol,
/// the dim depth and the two switches. Built in code rather than XAML so the layout has no
/// compiled-binding surface to break on a platform-specific build.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private readonly App _app;
    private readonly Slider _brightness = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider _dimDepth = new() { Minimum = 5, Maximum = 90, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly ComboBox _protocol = new();
    private readonly TextBox _workMinutes = new() { Width = 56 };
    private readonly TextBox _breakSeconds = new() { Width = 56 };
    private readonly CheckBox _showTimer = new() { Content = "Show timer (tray only)" };
    private readonly CheckBox _startWithWindows = new() { Content = "Start with Windows" };
    private readonly TextBlock _status = new() { Text = "—", Opacity = 0.75, FontSize = 12 };
    private readonly TextBlock _brightnessValue = new() { Text = "—", Width = 44, TextAlignment = TextAlignment.Right };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private int _pendingBrightness = -1;
    private bool _loading = true;

    private static readonly string[] Presets = ["20/20/20", "20/5", "45/5", "Custom"];

    public FlyoutWindow(App app)
    {
        _app = app;

        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        Background = new SolidColorBrush(Color.Parse("#1E1E22"));
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 10 };

        root.Children.Add(new TextBlock { Text = "Pomodim", FontSize = 16, FontWeight = FontWeight.SemiBold });
        root.Children.Add(_status);

        // Brightness
        var brightnessRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var brightnessLabel = new TextBlock { Text = "Brightness", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(brightnessLabel, 0);
        Grid.SetColumn(_brightnessValue, 1);
        brightnessRow.Children.Add(brightnessLabel);
        brightnessRow.Children.Add(_brightnessValue);
        root.Children.Add(brightnessRow);
        root.Children.Add(_brightness);

        // Protocol
        _protocol.ItemsSource = Presets;
        _protocol.SelectedIndex = 0;
        root.Children.Add(new TextBlock { Text = "Break protocol", FontSize = 12, Opacity = 0.7 });
        root.Children.Add(_protocol);

        var customRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        customRow.Children.Add(new TextBlock { Text = "work", VerticalAlignment = VerticalAlignment.Center });
        customRow.Children.Add(_workMinutes);
        customRow.Children.Add(new TextBlock { Text = "min · break", VerticalAlignment = VerticalAlignment.Center });
        customRow.Children.Add(_breakSeconds);
        customRow.Children.Add(new TextBlock { Text = "s", VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(customRow);

        // Dim depth
        var depthRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var depthLabel = new TextBlock { Text = "Break dim depth", VerticalAlignment = VerticalAlignment.Center };
        var depthValue = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(depthLabel, 0);
        Grid.SetColumn(depthValue, 1);
        depthRow.Children.Add(depthLabel);
        depthRow.Children.Add(depthValue);
        root.Children.Add(depthRow);
        root.Children.Add(_dimDepth);

        root.Children.Add(_showTimer);
        root.Children.Add(_startWithWindows);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var breakNow = new Button { Content = "Break now" };
        var close = new Button { Content = "Close" };
        buttons.Children.Add(breakNow);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        Content = root;

        breakNow.Click += (_, _) => { _app.StartBreakNow(); };
        close.Click += (_, _) => Hide();

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (_pendingBrightness >= 0 && !_loading)
            {
                _app.SetAllBrightness(_pendingBrightness);
            }
        };

        _brightness.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty)
            {
                return;
            }

            var value = (int)Math.Round(_brightness.Value);
            _brightnessValue.Text = $"{value}%";
            _pendingBrightness = value;
            if (!_loading)
            {
                _debounce.Stop();
                _debounce.Start();
            }
        };

        _dimDepth.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                depthValue.Text = $"{(int)Math.Round(_dimDepth.Value)}%";
                Persist();
            }
        };

        _protocol.SelectionChanged += (_, _) =>
        {
            var custom = _protocol.SelectedIndex == 3;
            _workMinutes.IsEnabled = custom;
            _breakSeconds.IsEnabled = custom;
            Persist();
        };

        _showTimer.IsCheckedChanged += (_, _) =>
        {
            if (!_loading && !_showTimer.IsChecked!.Value)
            {
                _app.ResetTrayText();
            }

            Persist();
        };
        _startWithWindows.IsCheckedChanged += (_, _) => Persist();
        _workMinutes.LostFocus += (_, _) => Persist();
        _breakSeconds.LostFocus += (_, _) => Persist();

        Deactivated += (_, _) => Hide();
        Closing += (_, e) => { e.Cancel = true; Hide(); };
    }

    /// <summary>Pushes the current state into the controls without echoing it back as a change.</summary>
    public void Refresh(BreakEngine engine, AppSettings settings)
    {
        var phase = engine.Phase == BreakPhase.Break ? "break" : "work";
        var remaining = engine.PhaseRemaining;
        var paused = engine.State == SessionState.Paused ? " (paused)" : string.Empty;
        _status.Text = $"{phase} · {remaining:mm\\:ss} left{paused}";

        if (_loading)
        {
            var displays = _app.GetDisplays().Where(d => d.Supported).ToList();
            if (displays.Count > 0)
            {
                _brightness.Value = displays[0].Current;
                _brightnessValue.Text = $"{displays[0].Current}%";
            }

            _dimDepth.Value = settings.DimDepth;
            _protocol.SelectedIndex = Array.IndexOf(Presets, settings.UseCustomProtocol ? "Custom" : settings.ProtocolName);
            if (_protocol.SelectedIndex < 0)
            {
                _protocol.SelectedIndex = 0;
            }

            _workMinutes.Text = settings.WorkMinutes.ToString();
            _breakSeconds.Text = settings.BreakSeconds.ToString();
            _showTimer.IsChecked = settings.ShowTimer;
            _startWithWindows.IsChecked = settings.StartWithWindows && Autostart.IsSupported;
            _startWithWindows.IsEnabled = Autostart.IsSupported;

            var custom = _protocol.SelectedIndex == 3;
            _workMinutes.IsEnabled = custom;
            _breakSeconds.IsEnabled = custom;

            _loading = false;
        }
    }

    private void Persist()
    {
        if (_loading)
        {
            return;
        }

        var settings = new AppSettings
        {
            UseCustomProtocol = _protocol.SelectedIndex == 3,
            ProtocolName = _protocol.SelectedIndex switch { 1 => "20/5", 2 => "45/5", 3 => "custom", _ => "20/20/20" },
            WorkMinutes = int.TryParse(_workMinutes.Text, out var work) ? Math.Clamp(work, 1, 600) : 22,
            BreakSeconds = int.TryParse(_breakSeconds.Text, out var brk) ? Math.Clamp(brk, 5, 7200) : 20,
            DimDepth = (int)Math.Round(_dimDepth.Value),
            ShowTimer = _showTimer.IsChecked == true,
            StartWithWindows = _startWithWindows.IsChecked == true,
        };

        _app.ApplySettings(settings);
    }

    /// <summary>Places the flyout above the taskbar, bottom-right of the primary screen.</summary>
    public void PlaceNearTray()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var width = (int)Math.Round(340 * scaling);
        // Bounds.Height is 0 until the first layout pass; fall back to a sane estimate so
        // the flyout is never placed off the bottom of the screen on first open.
        var logicalHeight = Bounds.Height >= 1 ? Bounds.Height : 340;
        var height = (int)Math.Round(logicalHeight * scaling);
        var x = area.X + area.Width - width - (int)Math.Round(12 * scaling);
        var y = area.Y + area.Height - height - (int)Math.Round(12 * scaling);
        Position = new PixelPoint(x, y);
    }
}
