using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFKit.Models;

namespace AFKit.Dialogs;

/// <summary>
/// V2 — battle window: transparent, draggable and resizable; one cell per profile
/// with its Pokémon and the enemy. Left click only drags; the right button has the Overlay.
/// </summary>
public partial class TaskbarWindow : Window
{
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(TaskbarWindow), new PropertyMetadata(1));

    /// <summary>1 account = 1 column; 2 or more = 2 columns (3–4 become 2×2, more than that grows in rows).</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public ObservableCollection<TaskbarCell> Items { get; } = new();

    /// <summary>The user turned the Overlay on/off.</summary>
    public event EventHandler<bool>? OverlayChanged;
    /// <summary>The user chose to hide the window.</summary>
    public event EventHandler? HideRequested;
    /// <summary>Position or size changed (already debounced, so it does not save on every pixel).</summary>
    public event EventHandler? BoundsChanged;

    private readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _effects = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer _boundsDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public TaskbarWindow(bool overlay)
    {
        InitializeComponent();
        Cells.ItemsSource = Items;
        Items.CollectionChanged += OnItemsChanged;
        SetOverlay(overlay);

        _animation.Tick += (_, _) => { foreach (var c in Items) c.Advance(); };
        _effects.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var playing = false;
            foreach (var c in Items) { c.TickEffect(now); playing |= c.EffectFrame != null; }
            if (!playing) _effects.Stop(); // only runs while there is an effect on screen
        };
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

        Loaded += (_, _) => _animation.Start();
        Closed += (_, _) => { _animation.Stop(); _effects.Stop(); _boundsDebounce.Stop(); };
        UpdateLayoutState();
    }

    /// <summary>Plays the move effect in the profile's cell.</summary>
    public void PlayEffect(string profileId, IReadOnlyList<ImageSource> frames, int ms)
    {
        var cell = Items.FirstOrDefault(c => c.ProfileId == profileId);
        if (cell == null || !cell.HasFoe) return;
        cell.PlayEffect(frames, ms);
        if (!_effects.IsEnabled) _effects.Start();
    }

    public void SetOverlay(bool overlay)
    {
        Topmost = overlay;
        OverlayItem.IsChecked = overlay;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateLayoutState();

    private void UpdateLayoutState()
    {
        Columns = Math.Max(1, Math.Min(2, Items.Count));
        EmptyHint.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestartBoundsDebounce()
    {
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
