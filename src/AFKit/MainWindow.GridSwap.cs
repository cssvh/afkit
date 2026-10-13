using System.Windows;
using AFKit.Controls;

namespace AFKit;

/// <summary>Grid mode: dragging a cell header onto another cell swaps the two (the order is saved).</summary>
public partial class MainWindow
{
    private ProfileView? _dropTarget;

    /// <summary>Grid cell (other than the dragged one) under the screen point.</summary>
    private ProfileView? GridCellAt(ProfileView dragged, Point screen)
    {
        if (!IsGridShown) return null;
        foreach (var p in _store.PinnedProfiles())
        {
            if (!_views.TryGetValue(p.Id, out var v) || ReferenceEquals(v, dragged)) continue;
            if (v.ScreenBounds().Contains(screen)) return v;
        }
        return null;
    }

    private void OnGridDragMoved(ProfileView dragged, Point screen)
    {
        var target = GridCellAt(dragged, screen);
        if (ReferenceEquals(target, _dropTarget)) return;
        _dropTarget?.SetDropTarget(false);
        _dropTarget = target;
        _dropTarget?.SetDropTarget(true);
    }

    private void OnGridDragEnded(ProfileView dragged, Point? screen)
    {
        _dropTarget?.SetDropTarget(false);
        _dropTarget = null;
        if (screen is not { } pt || GridCellAt(dragged, pt) is not { } target) return;

        var ids = _store.PinnedIds;
        var a = ids.IndexOf(dragged.Profile.Id);
        var b = ids.IndexOf(target.Profile.Id);
        if (a < 0 || b < 0 || a == b) return;
        (ids[a], ids[b]) = (ids[b], ids[a]);
        ApplyLayout();
        SaveStore();
    }
}
