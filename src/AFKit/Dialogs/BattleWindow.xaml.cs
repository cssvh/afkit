using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFKit.Models;

namespace AFKit.Dialogs;

/// <summary>
/// V2 — battle window of ONE profile: transparent, draggable and resizable.
/// Each profile has its own, with its own position, size and Overlay. Left click only drags;
/// the right button has the Overlay and "Hide the window" (turns them all off).
/// </summary>
public partial class BattleWindow : Window
{
    /// <summary>This window's profile.</summary>
    public string ProfileId { get; }

    /// <summary>The user turned this window's Overlay on/off.</summary>
    public event EventHandler<bool>? OverlayChanged;
    /// <summary>The user chose to hide the window (turns the whole feature off).</summary>
    public event EventHandler? HideRequested;
    /// <summary>Position or size changed (already debounced, so it does not save on every pixel).</summary>
    public event EventHandler? BoundsChanged;

    private readonly DispatcherTimer _boundsDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public BattleWindow(string profileId, TaskbarCell cell, bool overlay)
    {
        ProfileId = profileId;
        InitializeComponent();
        DataContext = cell;
        SetOverlay(overlay);

        _boundsDebounce.Tick += (_, _) => { _boundsDebounce.Stop(); BoundsChanged?.Invoke(this, EventArgs.Empty); };
        LocationChanged += (_, _) => RestartBoundsDebounce();
        SizeChanged += (_, _) => RestartBoundsDebounce();

        // Discreet frame only with the mouse over it (to see where the window is and drag/resize it).
        MouseEnter += (_, _) =>
        {
            Frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            Frame.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00));
        };
        MouseLeave += (_, _) =>
        {
            Frame.BorderBrush = Brushes.Transparent;
            Frame.Background = new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
        };

        // The info strip appeared: the window grows downward, keeping its width and scale.
        DrawingPanel.SizeChanged += (_, _) => FitHeightToInfo();
        Loaded += (_, _) => FitHeightToInfo(); // the strip may already be ready before the window opens

        Closed += (_, _) => _boundsDebounce.Stop();
    }

    /// <summary>Content margin (6) and frame (1), on both sides.</summary>
    private const double Chrome = 2 * (6 + 1);
    /// <summary>Width of the drawing (BattleCell's).</summary>
    private const double DrawingWidth = 300;

    private bool _fittedToInfo;

    /// <summary>
    /// The first time the strip appears in this opening, adjusts the Height so the whole drawing fits
    /// at the current width (Height = content × drawing height / 300 + margins), without exceeding the work area.
    /// The new size is saved through BoundsChanged.
    /// </summary>
    private void FitHeightToInfo()
    {
        if (_fittedToInfo || !IsLoaded || InfoStrip.ActualHeight < 1 || DrawingPanel.ActualHeight < 1) return;
        _fittedToInfo = true;
        Dispatcher.InvokeAsync(() =>
        {
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var scale = Math.Max(0.1, (width - Chrome) / DrawingWidth);
            var area = SystemParameters.WorkArea;
            var height = Math.Min(Math.Max(MinHeight, DrawingPanel.ActualHeight * scale + Chrome), area.Height);
            if (Math.Abs(height - ActualHeight) < 2) return;
            // Grew outside the work area: moves up as much as needed.
            if (Top + height > area.Bottom) Top = Math.Max(area.Top, area.Bottom - height);
            Height = height;
        }, DispatcherPriority.Loaded);
    }

    public void SetOverlay(bool overlay)
    {
        Topmost = overlay;
        OverlayItem.IsChecked = overlay;
    }

    private void RestartBoundsDebounce()
    {
        // Before it shows up, the changes are just the initial position (no need to save).
        if (!IsLoaded) return;
        _boundsDebounce.Stop();
        _boundsDebounce.Start();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }

    private void OnOverlayClicked(object sender, RoutedEventArgs e)
    {
        Topmost = OverlayItem.IsChecked;
        OverlayChanged?.Invoke(this, OverlayItem.IsChecked);
    }

    private void OnHideClicked(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);
}
