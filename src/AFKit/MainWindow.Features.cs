using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AFKit.Controls;
using AFKit.Models;
using AFKit.Services;

namespace AFKit;

/// <summary>
/// Sidebar and app features: CPU/memory usage, mute all, system tray, session restart after a crash,
/// language switching and profile reordering (drag and drop).
/// </summary>
public partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _inTray;
    private bool _exitRequested;
    private Rect _boundsBeforeTray;
    private bool _wasMaximizedBeforeTray;

    private void InitFeatures()
    {
        _usageTimer.Tick += OnUsageTick;
        // Windows shutdown/logoff: really close (it must not go to the tray and block the logoff).
        Application.Current.SessionEnding += (_, _) => _exitRequested = true;

        // Internet went down / came back (Wi-Fi, cable, VPN…): when it returns, reload the tabs that failed right away.
        _networkWasAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, _) => Dispatcher.BeginInvoke(new Action(CheckNetwork));
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(new Action(CheckNetwork));

        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/afkit.ico"));
            using var stream = info.Stream;
            _tray = new TrayIcon(new System.Drawing.Icon(stream));
            _tray.OpenRequested += (_, _) => RestoreFromTray();
            _tray.MuteAllToggled += (_, _) => ToggleMuteAll();
            _tray.CloseToTrayToggled += (_, _) =>
            {
                _store.CloseToTray = !_store.CloseToTray;
                UpdateTrayState();
                SaveStore();
            };
            _tray.ExitRequested += (_, _) => ExitApp();
            _tray.AutoRecoverToggled += (_, _) => ToggleAutoRecover();
            _tray.LanguageSelected += (_, code) => ChangeLanguage(code);
        }
        catch (Exception)
        {
            _tray = null; // no tray: the X button closes normally
        }
    }

    /// <summary>After afkit.json is loaded.</summary>
    private void OnStoreLoaded()
    {
        if (_tray == null) _store.CloseToTray = false;
        UpdateMuteAllButton();
        UpdateTrayState();
        _usageTimer.Start();
        InitTaskbar();
    }

    private void ShutdownFeatures()
    {
        _usageTimer.Stop();
        CloseBattleWindows();
        _tray?.Dispose();
        _tray = null;
    }

    // ================================================================ language

    /// <summary>Saves the chosen UI language and offers to restart so every text is rebuilt in it.</summary>
    internal void ChangeLanguage(string code)
    {
        if (Loc.Normalize(code) is not { } language || language == Loc.Language) return;
        _store.Language = language;
        SaveStore();

        // The new language was saved, so it also applies on the next launch if the user says no.
        var restart = MessageBox.Show(Loc.T("Main.LanguageRestart"), "AFKit", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (restart != MessageBoxResult.Yes) return;

        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--wait-pid {Environment.ProcessId}") { UseShellExecute = false });
        }
        catch (Exception) { return; }
        ExitApp();
    }

    private void OnLanguageButtonClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var (code, name) in Loc.Languages)
        {
            var item = new MenuItem { Header = name, IsCheckable = false, IsChecked = code == Loc.Language };
            item.Click += (_, _) => ChangeLanguage(code);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ================================================================ CPU and memory usage

    private readonly DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private Dictionary<int, TimeSpan> _lastCpu = new();
    private DateTime _lastSample = DateTime.UtcNow;
    private bool _sampling;

    private sealed record UsageSample(double CpuPercent, long MemoryBytes);

    private async void OnUsageTick(object? sender, EventArgs e)
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            // Process IDs come from WebView2 (UI thread); the measuring itself runs in the background.
            var pidsByProfile = _views.Values.ToDictionary(v => v.Profile, v => v.GetProcessIds());
            var now = DateTime.UtcNow;
            var elapsed = Math.Max(0.5, (now - _lastSample).TotalSeconds);
            _lastSample = now;
            var previous = _lastCpu;

            var (samples, current, own) = await Task.Run(() => Measure(pidsByProfile, previous, elapsed));
            _lastCpu = current;

            foreach (var profile in _store.Profiles)
            {
                if (profile.IsLoaded && samples.TryGetValue(profile, out var s))
                {
                    profile.UsageText = $"CPU {s.CpuPercent:0}% · {FormatBytes(s.MemoryBytes)}";
                    profile.MemoryText = FormatBytes(s.MemoryBytes);
                    profile.IsHeavy = s.CpuPercent >= 25 || s.MemoryBytes >= 1_500L * 1024 * 1024;
                }
                else
                {
                    profile.UsageText = "";
                    profile.MemoryText = "";
                    profile.IsHeavy = false;
                }
            }

            var totalCpu = samples.Values.Sum(s => s.CpuPercent) + own.CpuPercent;
            var totalMem = samples.Values.Sum(s => s.MemoryBytes) + own.MemoryBytes;
            UpdateFooter(totalCpu, totalMem);
        }
        catch (Exception)
        {
            // Measuring is informational only; it never takes the app down.
        }
        finally { _sampling = false; }
    }

    private static (Dictionary<Profile, UsageSample>, Dictionary<int, TimeSpan>, UsageSample) Measure(
        Dictionary<Profile, IReadOnlyList<int>> pidsByProfile, Dictionary<int, TimeSpan> previous, double elapsedSeconds)
    {
        var cores = Environment.ProcessorCount;
        var current = new Dictionary<int, TimeSpan>();
        var result = new Dictionary<Profile, UsageSample>();

        UsageSample MeasurePids(IEnumerable<int> pids)
        {
            double cpu = 0;
            long mem = 0;
            foreach (var pid in pids.Distinct())
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    var total = p.TotalProcessorTime;
                    if (previous.TryGetValue(pid, out var before))
                        cpu += Math.Max(0, (total - before).TotalSeconds) / (elapsedSeconds * cores) * 100;
                    current[pid] = total;
                    mem += p.WorkingSet64;
                }
                catch (Exception) { /* the process ended between listing and measuring */ }
            }
            return new UsageSample(cpu, mem);
        }

        foreach (var (profile, pids) in pidsByProfile)
            result[profile] = MeasurePids(pids);

        var own = MeasurePids(new[] { Environment.ProcessId });
        return (result, current, own);
    }

    private void UpdateFooter(double? cpu = null, long? memory = null)
    {
        var open = _store.Profiles.Count(p => p.IsLoaded);
        var text = Loc.T("Main.Footer", _store.Profiles.Count, open);
        if (cpu != null && memory != null)
        {
            text += $" · CPU {cpu:0}% · {FormatBytes(memory.Value)}";
            _tray?.SetTooltip(Loc.T("Tray.Tooltip", open, $"{cpu:0}", FormatBytes(memory.Value)));
        }
        FooterText.Text = text;
    }

    private static string FormatBytes(long bytes)
    {
        var mb = bytes / 1024.0 / 1024.0;
        return mb < 1024
            ? $"{mb:0} MB"
            : (mb / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
    }

    // ================================================================ mute all

    private void ToggleMuteAll()
    {
        _store.MuteAll = !_store.MuteAll;
        foreach (var view in _views.Values) view.RefreshMute();
        UpdateMuteAllButton();
        UpdateTrayState();
        SaveStore();
    }

    /// <summary>
    /// With "Mute everything" on, the user turned the sound on for ONE tab: the global switch goes off, but every other
    /// tab (of every profile) stays muted one by one — only that one plays again.
    /// </summary>
    private void UnmuteOnly(AFKit.Controls.ProfileView owner, AFKit.Controls.BrowserTab tab)
    {
        _store.MuteAll = false;
        foreach (var view in _views.Values) view.KeepOnlyAudible(ReferenceEquals(view, owner) ? tab : null);
        // Profiles with a closed session: they open with all tabs muted.
        foreach (var p in _store.Profiles.Where(p => !_views.ContainsKey(p.Id)))
            p.MutedTabIndexes = Enumerable.Range(0, p.SavedTabs.Count).ToList();
        UpdateMuteAllButton();
        UpdateTrayState();
        SaveStore();
    }

    private void OnMuteAllClicked(object sender, RoutedEventArgs e) => ToggleMuteAll();

    private void UpdateMuteAllButton()
    {
        var on = _store.MuteAll;
        MuteAllButton.Content = on ? "" : ""; // Segoe MDL2: mute / volume
        MuteAllButton.Foreground = (Brush)FindResource(on ? "Danger" : "FgDim");
        MuteAllButton.ToolTip = Loc.T(on ? "Main.UnmuteAll" : "Main.MuteAll");
    }

    // ================================================================ tray

    private void UpdateTrayState() => _tray?.SetState(_store.MuteAll, _store.CloseToTray, _store.AutoRecover);

    // ================================================================ automatic recovery

    private bool _networkWasAvailable;

    private void CheckNetwork()
    {
        var available = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        var cameBack = available && !_networkWasAvailable;
        _networkWasAvailable = available;
        // Even if "available" did not change (Wi-Fi switch, VPN), an address change is a good time to try again.
        if (available) foreach (var view in _views.Values) view.OnNetworkRestored();
        if (cameBack) _tray?.SetTooltip(Loc.T("Tray.NetworkBack"));
    }

    private void ToggleAutoRecover()
    {
        _store.AutoRecover = !_store.AutoRecover;
        foreach (var view in _views.Values) view.OnAutoRecoverChanged();
        UpdateTrayState();
        SaveStore();
    }

    /// <summary>
    /// Sends AFKit to the tray. The window is not hidden (that would make Chromium pause the pages):
    /// it leaves the taskbar and moves outside the visible area, with everything running normally.
    /// </summary>
    private void HideToTray()
    {
        if (_inTray || _tray == null) return;
        _wasMaximizedBeforeTray = WindowState == WindowState.Maximized;
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        _boundsBeforeTray = new Rect(Left, Top, Width, Height);

        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft - Width - 200;
        Top = SystemParameters.VirtualScreenTop;
        _inTray = true;

        if (!_store.TrayHintShown)
        {
            _tray.ShowBalloon(Loc.T("Tray.HintTitle"), Loc.T("Tray.HintText"));
            _store.TrayHintShown = true;
            SaveStore();
        }
    }

    public void RestoreFromTray()
    {
        if (_inTray)
        {
            _inTray = false;
            Left = _boundsBeforeTray.Left;
            Top = _boundsBeforeTray.Top;
            ShowInTaskbar = true;
            if (_wasMaximizedBeforeTray) WindowState = WindowState.Maximized;
            // The window spent a while off screen: pages in view may come back black without a repaint.
            foreach (var v in _views.Values) v.NudgeRepaint();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // brings it to the front even when another app has focus
        Topmost = false;
        Focus();
    }

    /// <summary>Closes for real (saving sessions), as "Exit AFKit" in the tray does. Used by the updater.</summary>
    internal void RequestExit() => ExitApp();

    private void ExitApp()
    {
        _exitRequested = true;
        Close();
    }

    // ================================================================ browser crash

    /// <summary>A profile's browser process crashed: reopens the session (tabs are restored).</summary>
    private async Task RestartCrashedSessionAsync(Profile profile)
    {
        if (!_views.ContainsKey(profile.Id)) return;
        var wasActive = profile == _active;
        await CloseSessionAsync(profile, reopenSoon: true);
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (wasActive || profile.IsPinned) await ReopenAsync(profile, wasActive);
        else EnsureView(profile); // keeps the session running in the background, as it was
        ApplyLayout();
    }

    // ================================================================ reorder profiles (drag and drop)

    private const string DragFormat = "AFKit.Profile";
    private Profile? _pressedProfile;
    private Point _pressPoint;
    private bool _dragging;

    private void OnProfileMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedProfile = ProfileFrom(sender);
        _pressPoint = e.GetPosition(Sections);
        _dragging = false;
    }

    private async void OnProfileMouseUp(object sender, MouseButtonEventArgs e)
    {
        // A plain click (no dragging) opens the profile.
        var p = ProfileFrom(sender);
        var pressed = _pressedProfile;
        _pressedProfile = null;
        if (_dragging || p == null || p != pressed) return;
        if (p != _active || (_store.GridMode && !p.IsPinned)) await ActivateAsync(p);
    }

    private void OnSidebarMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedProfile == null || e.LeftButton != MouseButtonState.Pressed || _dragging) return;
        var delta = e.GetPosition(Sections) - _pressPoint;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance &&
            Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        if (SearchBox.Text.Length > 0) return; // with a filter on, the visible order is not the real one

        _dragging = true;
        var profile = _pressedProfile;
        try
        {
            DragDrop.DoDragDrop(Sections, new DataObject(DragFormat, profile), DragDropEffects.Move);
        }
        finally
        {
            _pressedProfile = null;
            _dragging = false;
        }
    }

    private static Profile? DraggedProfile(DragEventArgs e) => e.Data.GetData(DragFormat) as Profile;

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        var target = ProfileFrom(sender);
        var dragged = DraggedProfile(e);
        e.Handled = true;
        if (sender is not Border row || target == null || dragged == null || target == dragged)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        // Insertion line above or below, depending on which half the mouse is in.
        var below = e.GetPosition(row).Y > row.ActualHeight / 2;
        row.BorderThickness = below ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
    }

    private void OnHeaderDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border header || DraggedProfile(e) == null)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;
        header.BorderThickness = new Thickness(0, 0, 0, 2);
    }

    private void OnDropTargetLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.BorderThickness = new Thickness(0);
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border row) return;
        row.BorderThickness = new Thickness(0);
        var target = ProfileFrom(sender);
        var dragged = DraggedProfile(e);
        if (target == null || dragged == null || target == dragged) return;

        var below = e.GetPosition(row).Y > row.ActualHeight / 2;
        var siblings = _store.ProfilesInGroup(target.GroupId).Where(p => p != dragged).ToList();
        var index = siblings.IndexOf(target) + (below ? 1 : 0);
        FinishMove(dragged, target.GroupId, index);
    }

    private void OnHeaderDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is Border header) header.BorderThickness = new Thickness(0);
        if ((sender as FrameworkElement)?.DataContext is not SidebarSection section || DraggedProfile(e) is not { } dragged)
            return;
        // Dropping on the group header: goes to the top of that group.
        FinishMove(dragged, section.Group?.Id, 0);
        if (section.Group is { IsExpanded: false } g) g.IsExpanded = true;
    }

    private void FinishMove(Profile profile, string? groupId, int index)
    {
        _store.MoveProfile(profile, groupId, index);
        SaveStore();
        RefreshSidebar();
    }

    private void MoveBy(Profile profile, int delta)
    {
        var list = _store.ProfilesInGroup(profile.GroupId).ToList();
        var i = list.IndexOf(profile);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        FinishMove(profile, profile.GroupId, j);
    }

    private void OnMoveUpMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is { } p) MoveBy(p, -1);
    }

    private void OnMoveDownMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is { } p) MoveBy(p, +1);
    }
}
