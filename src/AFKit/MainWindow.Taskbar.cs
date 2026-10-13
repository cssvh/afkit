using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFKit.Dialogs;
using AFKit.Models;
using AFKit.Services;

namespace AFKit;

/// <summary>
/// V2 — task bar: an independent battle window PER PROFILE, showing its Pokémon and the enemy.
/// Turned on/off per profile (right-click the profile) and from the tray menu (the windows and "all profiles").
/// Each window has its own position, size and Overlay, saved in the profile.
/// </summary>
public partial class MainWindow
{
    /// <summary>Open battle windows, by profile Id.</summary>
    private readonly Dictionary<string, BattleWindow> _battleWindows = new();
    /// <summary>Cells by profile Id (reused so the animation does not restart).</summary>
    private readonly Dictionary<string, TaskbarCell> _battleCells = new();

    // One pair of timers for all cells: sprite frames and move effect (only while one is playing).
    private readonly DispatcherTimer _battleAnimation = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _battleEffects = new() { Interval = TimeSpan.FromMilliseconds(30) };

    private const double BattleDefaultWidth = 300;
    /// <summary>Battle (170) + the info strip below; the window adjusts when the strip appears.</summary>
    private const double BattleDefaultHeight = 400;
    private const double BattleGap = 12;
    private const double BattleMargin = 16;

    /// <summary>After afkit.json is loaded.</summary>
    private void InitTaskbar()
    {
        if (_tray != null)
        {
            _tray.TaskbarToggled += (_, _) => SetTaskbarEnabled(!_store.TaskbarEnabled, fromTray: true);
            _tray.TaskbarAllProfiles += (_, _) => SetTaskbarForAll(true);
            _tray.TaskbarNoProfiles += (_, _) => SetTaskbarForAll(false);
        }

        _battleAnimation.Tick += (_, _) =>
        {
            foreach (var id in _battleWindows.Keys)
                if (_battleCells.TryGetValue(id, out var c)) c.Advance();
        };
        _battleEffects.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var playing = false;
            foreach (var c in _battleCells.Values) { c.TickEffect(now); playing |= c.EffectFrame != null; }
            if (!playing) _battleEffects.Stop(); // only runs while there is an effect on screen
        };

        UpdateTrayTaskbarState();
        if (_store.TaskbarEnabled) RefreshTaskbar();
    }

    private void SetTaskbarEnabled(bool enabled, bool fromTray = false)
    {
        _store.TaskbarEnabled = enabled;
        if (enabled)
        {
            RefreshTaskbar();
            // Pages that were already open resend everything (sprites are only sent once per page).
            foreach (var view in _views.Values) view.RequestTaskbarResync();
            if (fromTray && _battleWindows.Count == 0)
                _tray?.ShowBalloon("AFKit", Loc.T("Taskbar.NoneOpen"));
        }
        else
        {
            CloseBattleWindows();
            foreach (var view in _views.Values) view.RefreshPageSignals(); // "afkit-info:0": the pages stop sending
        }
        UpdateTrayTaskbarState();
        SaveStore();
    }

    private void SetTaskbarForAll(bool show)
    {
        foreach (var p in _store.Profiles) p.ShowInTaskbar = show;
        if (show && !_store.TaskbarEnabled) SetTaskbarEnabled(true);
        else SaveStore();
        RefreshTaskbar();
    }

    private void OnToggleTaskbarMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;
        p.ShowInTaskbar = !p.ShowInTaskbar;
        // Turned a profile on while the windows are off: turn the feature on so it shows up.
        if (p.ShowInTaskbar && !_store.TaskbarEnabled) SetTaskbarEnabled(true);
        else SaveStore();
        RefreshTaskbar();
    }

    private void UpdateTrayTaskbarState() => _tray?.SetTaskbarState(_store.TaskbarEnabled);

    // ================================================================ windows (one per profile)

    /// <summary>
    /// Opens/updates/closes the windows: one per enabled profile, with an open session and PokeIdle in a tab.
    /// </summary>
    private void RefreshTaskbar()
    {
        // The task bar tab and the options change through here: each profile tells its pages (only posts what changed).
        foreach (var v in _views.Values) v.RefreshPageSignals();

        if (!_store.TaskbarEnabled)
        {
            if (_battleWindows.Count > 0) CloseBattleWindows();
            return;
        }

        var wanted = new HashSet<string>();
        foreach (var profile in OrderedProfiles())
        {
            if (!profile.ShowInTaskbar || !_views.TryGetValue(profile.Id, out var view)) continue;
            if (view.GetTaskbarData() is not { } data) continue;
            GameSnapshot game = data.Game;
            IReadOnlyList<ImageSource> meFrames = data.MeFrames;
            IReadOnlyList<ImageSource> foeFrames = data.FoeFrames;
            wanted.Add(profile.Id);

            if (!_battleCells.TryGetValue(profile.Id, out var cell))
            {
                cell = new TaskbarCell { ProfileId = profile.Id };
                _battleCells[profile.Id] = cell;
            }

            // --- cell filling (visual) ---
            var me = game.Me!;
            cell.MeText = Label(me);
            cell.SetMe(me, meFrames);

            var foe = game.Foe;
            cell.FoeText = foe == null ? "" : Label(foe);
            cell.SetFoe(foe, foeFrames);
            cell.Advance();

            // Strip below the battle: session summary, record and latest captures (from the grid script).
            var info = view.GetTaskbarInfo();
            cell.SetInfo(info.Summary, info.Captures);
            // --- end of cell filling ---

            if (_battleWindows.TryGetValue(profile.Id, out var window))
                window.Title = "AFKit — " + profile.Name;
            else
                OpenBattleWindow(profile, cell);
        }

        // Profile turned off, session closed or game tab closed: close its window.
        foreach (var id in _battleWindows.Keys.Where(id => !wanted.Contains(id)).ToList()) CloseBattleWindow(id);
        foreach (var id in _battleCells.Keys.Where(id => !wanted.Contains(id)).ToList()) _battleCells.Remove(id);
        UpdateBattleTimers();
    }

    private void OpenBattleWindow(Profile profile, TaskbarCell cell)
    {
        var w = new BattleWindow(profile.Id, cell, profile.TaskbarOverlay)
        {
            Title = "AFKit — " + profile.Name,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        if (profile.TaskbarBounds is { Length: 4 } b && IsOnScreen(b))
        {
            w.Left = b[0];
            w.Top = b[1];
            w.Width = Math.Max(w.MinWidth, b[2]);
            w.Height = Math.Max(w.MinHeight, b[3]);
        }
        else
        {
            var place = DefaultBattlePlace();
            w.Left = place.X;
            w.Top = place.Y;
            w.Width = BattleDefaultWidth;
            w.Height = BattleDefaultHeight;
        }

        w.OverlayChanged += (_, on) => { profile.TaskbarOverlay = on; SaveStore(); };
        w.HideRequested += (_, _) => SetTaskbarEnabled(false);
        w.BoundsChanged += (_, _) =>
        {
            profile.TaskbarBounds = new[] { w.Left, w.Top, w.Width, w.Height };
            SaveStore();
        };
        // Closed from outside (e.g. Alt+F4): forget the window; it comes back on the next RefreshTaskbar.
        w.Closed += (_, _) =>
        {
            if (_battleWindows.TryGetValue(profile.Id, out var cur) && ReferenceEquals(cur, w))
            {
                _battleWindows.Remove(profile.Id);
                UpdateBattleTimers();
            }
        };

        _battleWindows[profile.Id] = w;
        w.Show();
    }

    /// <summary>
    /// A profile's first time: cascading from the bottom-right corner of the work area,
    /// going up one window at a time and moving to the left column when it no longer fits.
    /// Takes the first spot that does not touch any window already open.
    /// </summary>
    private Point DefaultBattlePlace()
    {
        var area = SystemParameters.WorkArea;
        var stepX = BattleDefaultWidth + BattleGap;
        var stepY = BattleDefaultHeight + BattleGap;
        var rows = Math.Max(1, (int)((area.Height - BattleMargin + BattleGap) / stepY));
        var cols = Math.Max(1, (int)((area.Width - BattleMargin + BattleGap) / stepX));
        var open = _battleWindows.Values.Select(o => new Rect(o.Left, o.Top, o.Width, o.Height)).ToList();

        for (var i = 0; i < rows * cols; i++)
        {
            var col = i / rows;
            var row = i % rows;
            var x = area.Right - BattleMargin - BattleDefaultWidth - col * stepX;
            var y = area.Bottom - BattleMargin - BattleDefaultHeight - row * stepY;
            var slot = new Rect(x, y, BattleDefaultWidth, BattleDefaultHeight);
            if (!open.Any(r => r.IntersectsWith(slot))) return new Point(x, y);
        }
        // Screen full of windows: take the first spot anyway.
        return new Point(area.Right - BattleMargin - BattleDefaultWidth, area.Bottom - BattleMargin - BattleDefaultHeight);
    }

    private void CloseBattleWindow(string profileId)
    {
        if (!_battleWindows.Remove(profileId, out var w)) return;
        w.Close();
    }

    /// <summary>Closes all windows (feature turned off or AFKit exiting).</summary>
    private void CloseBattleWindows()
    {
        foreach (var id in _battleWindows.Keys.ToList()) CloseBattleWindow(id);
        _battleCells.Clear();
        UpdateBattleTimers();
    }

    private void UpdateBattleTimers()
    {
        if (_battleWindows.Count == 0)
        {
            _battleAnimation.Stop();
            _battleEffects.Stop();
        }
        else if (!_battleAnimation.IsEnabled)
        {
            _battleAnimation.Start();
        }
    }

    private static bool IsOnScreen(double[] b) =>
        b[0] + b[2] > SystemParameters.VirtualScreenLeft + 40 &&
        b[1] + b[3] > SystemParameters.VirtualScreenTop + 40 &&
        b[0] < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
        b[1] < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;

    /// <summary>Your Pokémon hit: plays the move effect over the enemy, if the profile has an open window.</summary>
    private void OnTaskbarHit(Profile profile, AFKit.Controls.ProfileView view, string fxKey)
    {
        if (!_store.TaskbarEnabled || !profile.ShowInTaskbar) return;
        if (!_battleWindows.ContainsKey(profile.Id) || !_battleCells.TryGetValue(profile.Id, out var cell)) return;
        if (!cell.HasFoe) return;
        if (view.GetSprite(fxKey) is not { } fx) return;
        // (the first hit of each type may go without an effect while its sheet is loading)
        cell.PlayEffect(fx.Frames, fx.Ms);
        if (!_battleEffects.IsEnabled) _battleEffects.Start();
    }

    private static string Label(PokemonInfo p) =>
        (p.Shiny ? "✨ " : "") + p.Name + (p.Level is { } lv ? $" {Loc.T("Taskbar.LevelShort")} {lv}" : "");

    /// <summary>Profiles in sidebar order (groups, then "No group").</summary>
    private IEnumerable<Profile> OrderedProfiles()
    {
        foreach (var g in _store.Groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            foreach (var p in _store.ProfilesInGroup(g.Id)) yield return p;
        foreach (var p in _store.ProfilesInGroup(null)) yield return p;
    }
}
