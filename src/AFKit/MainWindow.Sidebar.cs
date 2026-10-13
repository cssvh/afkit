using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AFKit.Services;

namespace AFKit;

/// <summary>
/// Sidebar that collapses with a button (or Ctrl+B): open, or collapsed into an icon strip with the avatars.
/// Since WebView2 is a native window and always draws on top of WPF, the sidebar cannot float over the page:
/// it pushes the content (changes the column width), with a short animation of a few steps.
/// </summary>
public partial class MainWindow
{
    public static readonly DependencyProperty SidebarCompactProperty = DependencyProperty.Register(
        nameof(SidebarCompact), typeof(bool), typeof(MainWindow),
        new PropertyMetadata(false, (d, _) => ((MainWindow)d).ApplySidebarCompactChrome()));

    /// <summary>True while the sidebar shows only the icon strip (the sidebar templates bind to this).</summary>
    public bool SidebarCompact
    {
        get => (bool)GetValue(SidebarCompactProperty);
        set => SetValue(SidebarCompactProperty, value);
    }

    private const double SidebarFullWidth = 250;
    private const double SidebarRailWidth = 56;
    private const double SidebarAnimationMs = 130;

    // A few steps only: each step resizes the WebView2s in the main area, which is expensive.
    private readonly DispatcherTimer _sidebarAnimTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch _sidebarAnimClock = new();
    private double _sidebarAnimFrom;
    private double _sidebarAnimTo = SidebarFullWidth;
    private bool _sidebarExpanded = true;

    /// <summary>After afkit.json is loaded.</summary>
    private void InitSidebar()
    {
        _sidebarAnimTimer.Tick += OnSidebarAnimTick;
        if (_store.SidebarCollapsed) SetSidebarExpanded(false, animate: false);
        UpdateSidebarToggleButton();
    }

    // ================================================================ show / hide button

    /// <summary>Menu button (☰) or Ctrl+B: collapses into an icon strip or opens again.</summary>
    private void ToggleSidebar()
    {
        _store.SidebarCollapsed = _sidebarExpanded;
        SetSidebarExpanded(!_store.SidebarCollapsed);
        UpdateSidebarToggleButton();
        SaveStore();
    }

    private void OnSidebarToggleClicked(object sender, RoutedEventArgs e) => ToggleSidebar();

    private void UpdateSidebarToggleButton()
    {
        SidebarToggleButton.ToolTip = Loc.T(_sidebarExpanded ? "Main.HideMenuLong" : "Main.ShowMenu");
    }

    // ================================================================ width and look

    /// <summary>Opens (250 px) or collapses (icon strip) the sidebar, pushing the main area.</summary>
    private void SetSidebarExpanded(bool expanded, bool animate = true)
    {
        _sidebarExpanded = expanded;
        var target = expanded ? SidebarFullWidth : SidebarRailWidth;

        if (!expanded)
        {
            // Collapsing: switch to the strip right at the start (the text does not get squeezed during the animation).
            SidebarCompact = true;
            if (SearchBox.Text.Length > 0) SearchBox.Text = ""; // a hidden filter would be confusing
        }

        var current = SidebarColumn.Width.IsAbsolute ? SidebarColumn.Width.Value : SidebarColumn.ActualWidth;
        if (!animate || !IsLoaded || Math.Abs(current - target) < 1)
        {
            FinishSidebarAnimation(target);
            return;
        }

        _sidebarAnimFrom = current;
        _sidebarAnimTo = target;
        _sidebarAnimClock.Restart();
        if (!_sidebarAnimTimer.IsEnabled) _sidebarAnimTimer.Start();
    }

    private void OnSidebarAnimTick(object? sender, EventArgs e)
    {
        var t = Math.Min(1, _sidebarAnimClock.Elapsed.TotalMilliseconds / SidebarAnimationMs);
        if (t >= 1)
        {
            FinishSidebarAnimation(_sidebarAnimTo);
            return;
        }
        var eased = 1 - Math.Pow(1 - t, 3); // ease-out
        SidebarColumn.Width = new GridLength(Math.Round(_sidebarAnimFrom + (_sidebarAnimTo - _sidebarAnimFrom) * eased));
    }

    private void FinishSidebarAnimation(double target)
    {
        _sidebarAnimTimer.Stop();
        _sidebarAnimClock.Reset();
        SidebarColumn.Width = new GridLength(target);
        // Opening: the text only comes back once the sidebar has its full width.
        SidebarCompact = !_sidebarExpanded;
    }

    /// <summary>Shows/hides the parts of the sidebar that do not fit in the icon strip.</summary>
    private void ApplySidebarCompactChrome()
    {
        var compact = SidebarCompact;
        SidebarTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        // Hidden (not Collapsed), like the search box: the second header row keeps its height.
        SidebarHeaderButtons.Visibility = compact ? Visibility.Hidden : Visibility.Visible;
        // Hidden (not Collapsed): keeps the height, so the avatars do not jump when the sidebar opens.
        SearchBox.Visibility = compact ? Visibility.Hidden : Visibility.Visible;
        SidebarFooter.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarScroll.VerticalScrollBarVisibility = compact ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        Sections.Margin = compact ? new Thickness(4, 0, 4, 10) : new Thickness(6, 0, 6, 10);
    }
}
