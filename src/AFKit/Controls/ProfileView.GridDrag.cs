using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AFKit.Services;

namespace AFKit.Controls;

/// <summary>
/// Grid mode: the cell frame uses the profile color, and the cell header can be dragged onto another
/// cell to swap the two.
/// The cell content is a WebView2 (native window), which does not get WPF drag and drop. So the
/// drag is done "by hand": the header captures the mouse and MainWindow finds the cell under the cursor from its
/// screen position.
/// </summary>
public partial class ProfileView
{
    private bool _gridFocused;
    private bool _dropTarget;

    /// <summary>Dragging the header: cursor position on screen (MainWindow highlights the target cell).</summary>
    public event EventHandler<Point>? GridDragMoved;
    /// <summary>Header released: screen position, or null if the drag was cancelled (Esc).</summary>
    public event EventHandler<Point?>? GridDragEnded;

    private Point? _dragStart;
    private bool _dragging;

    private void InitGridDrag()
    {
        GridHeader.MouseLeftButtonDown += OnGridHeaderDown;
        GridHeader.MouseMove += OnGridHeaderMove;
        GridHeader.MouseLeftButtonUp += OnGridHeaderUp;
        GridHeader.LostMouseCapture += (_, _) => { if (_dragging) EndGridDrag(null); _dragStart = null; };
        GridHeader.KeyDown += (_, e) => { if (_dragging && e.Key == Key.Escape) { EndGridDrag(null); e.Handled = true; } };
        GridHeader.FocusVisualStyle = null;
        GridHeader.ToolTip = Loc.T("Grid.DragTooltip");
    }

    /// <summary>The profile color (falls back to the default blue if the saved color is invalid).</summary>
    private Color ProfileColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(Profile.Color); }
        catch (Exception) { return Color.FromRgb(0x5B, 0x8D, 0xEF); }
    }

    /// <summary>
    /// Grid cell frame in the profile color: 2 px and full color on the focused cell; 1 px and a softer color
    /// on the others (with 1 px of slack so the content does not "jump"). Drag target: 2 px in white.
    /// </summary>
    private void ApplyGridFrame()
    {
        if (!_inGrid)
        {
            Frame.CornerRadius = new CornerRadius(0);
            Frame.BorderThickness = new Thickness(0);
            Frame.Padding = new Thickness(0);
            return;
        }
        var c = ProfileColor();
        var strong = _gridFocused || _dropTarget;
        Brush brush = _dropTarget
            ? Brushes.White
            : new SolidColorBrush(_gridFocused ? c : Color.FromArgb(0x99, c.R, c.G, c.B));
        if (brush.CanFreeze) brush.Freeze();
        Frame.CornerRadius = new CornerRadius(10);
        Frame.BorderThickness = new Thickness(strong ? 2 : 1);
        Frame.Padding = new Thickness(strong ? 0 : 1);
        Frame.BorderBrush = brush;
    }

    /// <summary>MainWindow marks/unmarks this cell as the drag target.</summary>
    public void SetDropTarget(bool on)
    {
        if (_dropTarget == on) return;
        _dropTarget = on;
        ApplyGridFrame();
    }

    private void OnGridHeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (!_inGrid) return;
        _dragStart = e.GetPosition(this);
        GridHeader.CaptureMouse();
        GridHeader.Focusable = true;
        GridHeader.Focus(); // so Esc gets here
    }

    private void OnGridHeaderMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - start.X) < SystemParameters.MinimumHorizontalDragDistance * 2 &&
                Math.Abs(pos.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _dragging = true;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        GridDragMoved?.Invoke(this, PointToScreen(pos));
    }

    private void OnGridHeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) EndGridDrag(PointToScreen(e.GetPosition(this)));
        _dragStart = null;
        if (GridHeader.IsMouseCaptured) GridHeader.ReleaseMouseCapture();
    }

    private void EndGridDrag(Point? screen)
    {
        if (!_dragging) return;
        _dragging = false;
        _dragStart = null;
        Mouse.OverrideCursor = null;
        if (GridHeader.IsMouseCaptured) GridHeader.ReleaseMouseCapture();
        GridDragEnded?.Invoke(this, screen);
    }

    /// <summary>This cell's rectangle on screen (so MainWindow can find the cell under the cursor).</summary>
    public Rect ScreenBounds()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0 || PresentationSource.FromVisual(this) == null) return Rect.Empty;
        var a = PointToScreen(new Point(0, 0));
        var b = PointToScreen(new Point(ActualWidth, ActualHeight));
        return new Rect(a, b);
    }
}
