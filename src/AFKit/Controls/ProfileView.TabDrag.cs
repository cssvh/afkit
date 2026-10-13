using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AFKit.Controls;

/// <summary>
/// Reorder tabs by dragging (like Chrome): the tab follows the mouse and swaps places with its neighbor
/// as soon as the cursor passes its middle. Uses mouse capture on the tab strip (not OLE DragDrop),
/// which is smoother and does not fight with WebView2's native windows.
/// Also: "Move left/right" in the tab menu and Ctrl+Shift+PageUp/PageDown.
/// </summary>
public partial class ProfileView
{
    /// <summary>The tab order changed (MainWindow saves).</summary>
    public event EventHandler? TabsReordered;

    private const double TabDragOpacity = 0.85;
    private const double AutoScrollEdge = 28;   // distance from the edge at which the strip starts scrolling
    private const double AutoScrollMaxStep = 18; // pixels per step, right against the edge

    private BrowserTab? _pressedTab;   // tab where the left button was pressed (drag candidate)
    private Point _tabPressPoint;      // click position, relative to the tab strip
    private bool _tabDragging;
    private int _tabDragStartIndex;
    private UIElement? _tabDragContainer;
    private DispatcherTimer? _tabAutoScrollTimer;

    // ------------------------------------------------------------------ dragging

    /// <summary>Click on the tab: only notes the point. Selecting stays in MouseLeftButtonDown (instant).</summary>
    private void OnTabPressed(object sender, MouseButtonEventArgs e)
    {
        _pressedTab = null;
        if (_tabDragging || _inGrid) return;
        if (sender is not FrameworkElement element || element.DataContext is not BrowserTab tab) return;
        // Buttons inside the tab (close, speaker) do not start a drag.
        if (IsInsideButton(e.OriginalSource as DependencyObject, element)) return;
        _pressedTab = tab;
        _tabPressPoint = e.GetPosition(TabStrip);
    }

    private static bool IsInsideButton(DependencyObject? source, DependencyObject stop)
    {
        for (var d = source; d != null && d != stop; d = d is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(d)
                 : LogicalTreeHelper.GetParent(d))
        {
            if (d is ButtonBase) return true;
        }
        return false;
    }

    private void OnTabStripMouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragging)
        {
            UpdateTabDragPosition(e.GetPosition(TabStrip).X);
            UpdateTabAutoScroll();
            return;
        }

        if (_pressedTab == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _pressedTab = null; return; }
        var delta = e.GetPosition(TabStrip) - _tabPressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        BeginTabDrag(_pressedTab);
    }

    private void OnTabStripMouseUp(object sender, MouseButtonEventArgs e)
    {
        _pressedTab = null;
        if (!_tabDragging) return;
        EndTabDrag(cancel: false);
        e.Handled = true;
    }

    private void OnTabStripLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Capture lost without releasing the button (Alt+Tab, another window…): the tab stays where it is.
        if (_tabDragging) EndTabDrag(cancel: false);
    }

    private void BeginTabDrag(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) { _pressedTab = null; return; }

        _tabDragging = true;
        _tabDragStartIndex = index;
        _pressedTab = null;
        if (!TabStrip.CaptureMouse())
        {
            _tabDragging = false;
            return;
        }
        // Focus on the strip during the drag so Esc reaches us (WebView2 would swallow the key).
        TabStrip.Focus();
        ApplyTabDragVisual(tab);
    }

    private void EndTabDrag(bool cancel)
    {
        if (!_tabDragging) return;
        _tabDragging = false; // before releasing the capture: LostMouseCapture must not come in again
        _tabAutoScrollTimer?.Stop();

        var tab = _tabDragContainer is FrameworkElement fe ? fe.DataContext as BrowserTab : null;
        if (_tabDragContainer != null) _tabDragContainer.Opacity = 1;
        _tabDragContainer = null;
        if (TabStrip.IsMouseCaptured) TabStrip.ReleaseMouseCapture();

        var index = tab == null ? -1 : _tabs.IndexOf(tab);
        if (cancel && index >= 0 && index != _tabDragStartIndex && _tabDragStartIndex < _tabs.Count)
        {
            _tabs.Move(index, _tabDragStartIndex);
            index = _tabDragStartIndex;
        }

        // Give the focus back to the page in view.
        if (_selected != null && _selected == ShownTab && !_closing) _selected.WebView.Focus();

        if (index >= 0 && index != _tabDragStartIndex) TabsReordered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Esc during the drag: puts the tab back where it was. Returns true if there was a drag.</summary>
    private bool CancelTabDrag()
    {
        if (!_tabDragging) return false;
        EndTabDrag(cancel: true);
        return true;
    }

    private void ApplyTabDragVisual(BrowserTab tab)
    {
        if (_tabDragContainer != null) _tabDragContainer.Opacity = 1;
        _tabDragContainer = TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as UIElement;
        if (_tabDragContainer != null) _tabDragContainer.Opacity = TabDragOpacity;
    }

    /// <summary>
    /// Swaps the dragged tab with its neighbor whenever the cursor passes the neighbor's middle.
    /// <paramref name="x"/> is relative to the tab strip (scrolling already accounted for).
    /// </summary>
    private void UpdateTabDragPosition(double x)
    {
        var tab = _tabDragContainer is FrameworkElement fe ? fe.DataContext as BrowserTab : null;
        var i = tab == null ? -1 : _tabs.IndexOf(tab);
        if (i < 0) { EndTabDrag(cancel: false); return; } // the tab was closed in the middle of the drag

        var moved = false;
        while (i < _tabs.Count - 1)
        {
            var (left, width) = SlotBounds(i + 1);
            if (x <= left + width / 2) break;
            _tabs.Move(i, i + 1);
            i++;
            moved = true;
        }
        while (i > 0)
        {
            var (left, width) = SlotBounds(i - 1);
            if (x >= left + width / 2) break;
            _tabs.Move(i, i - 1);
            i--;
            moved = true;
        }
        // The container is normally the same after the Move, but reapply to be safe.
        if (moved) ApplyTabDragVisual(tab!);
    }

    /// <summary>Position (left) and width of the tab slot at the given index, adding up the widths in the current order.</summary>
    private (double Left, double Width) SlotBounds(int index)
    {
        double left = 0;
        for (var k = 0; k < index; k++) left += SlotWidth(_tabs[k]);
        return (left, SlotWidth(_tabs[index]));
    }

    private double SlotWidth(BrowserTab tab) =>
        TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement c && c.ActualWidth > 0
            ? c.ActualWidth
            : 220;

    // ------------------------------------------------------------------ scroll the strip at the edges

    private void UpdateTabAutoScroll()
    {
        if (AutoScrollStep() == 0) { _tabAutoScrollTimer?.Stop(); return; }
        if (_tabAutoScrollTimer == null)
        {
            _tabAutoScrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            _tabAutoScrollTimer.Tick += (_, _) =>
            {
                var step = _tabDragging ? AutoScrollStep() : 0;
                if (step == 0) { _tabAutoScrollTimer!.Stop(); return; }
                TabsScroll.ScrollToHorizontalOffset(TabsScroll.HorizontalOffset + step);
                TabsScroll.UpdateLayout();
                UpdateTabDragPosition(Mouse.GetPosition(TabStrip).X);
            };
        }
        if (!_tabAutoScrollTimer.IsEnabled) _tabAutoScrollTimer.Start();
    }

    /// <summary>How much to scroll per step: negative near the left edge, positive near the right, 0 in the middle.</summary>
    private double AutoScrollStep()
    {
        if (TabsScroll.ScrollableWidth <= 0) return 0;
        var x = Mouse.GetPosition(TabsScroll).X;
        var width = TabsScroll.ActualWidth;
        if (x < AutoScrollEdge && TabsScroll.HorizontalOffset > 0)
            return -AutoScrollMaxStep * Math.Min(1, (AutoScrollEdge - x) / AutoScrollEdge);
        if (x > width - AutoScrollEdge && TabsScroll.HorizontalOffset < TabsScroll.ScrollableWidth)
            return AutoScrollMaxStep * Math.Min(1, (x - (width - AutoScrollEdge)) / AutoScrollEdge);
        return 0;
    }

    // ------------------------------------------------------------------ move via menu / keyboard

    /// <summary>Moves the tab one position left (-1) or right (+1). At the ends it does nothing.</summary>
    public void MoveTab(BrowserTab? tab, int delta)
    {
        if (tab == null || _tabDragging) return;
        var i = _tabs.IndexOf(tab);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _tabs.Count) return;
        _tabs.Move(i, j);
        // Keeps the tab in view in the strip (after the layout adjusts).
        Dispatcher.InvokeAsync(() =>
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement c) c.BringIntoView();
        }, DispatcherPriority.Loaded);
        TabsReordered?.Invoke(this, EventArgs.Empty);
    }

    private void OnMoveTabLeftMenu(object sender, RoutedEventArgs e) => MoveTab(TabFrom(sender), -1);
    private void OnMoveTabRightMenu(object sender, RoutedEventArgs e) => MoveTab(TabFrom(sender), +1);

    /// <summary>Disables "Move left/right" when the tab is already at the end.</summary>
    private void UpdateMoveTabMenuItems(ContextMenu menu, BrowserTab tab)
    {
        var i = _tabs.IndexOf(tab);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is "move:-1") item.IsEnabled = i > 0;
            else if (item.Tag is "move:1") item.IsEnabled = i >= 0 && i < _tabs.Count - 1;
        }
    }
}
