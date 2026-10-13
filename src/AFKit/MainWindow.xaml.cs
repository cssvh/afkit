using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AFKit.Controls;
using AFKit.Dialogs;
using AFKit.Models;
using AFKit.Services;
using Microsoft.Win32;

namespace AFKit;

/// <summary>Sidebar item: a group (or "No group") with its profiles.</summary>
public sealed class SidebarSection
{
    public ProfileGroup? Group { get; init; }
    public string Title => Group?.Name ?? Loc.T("Main.NoGroup");
    public string Color => Group?.Color ?? "#4B5563";
    public bool IsRealGroup => Group != null;
    public bool IsExpanded { get; init; }
    public List<Profile> Profiles { get; init; } = new();
    public string CountText => Profiles.Count.ToString();
}

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly ProfileArchiveService _archive;
    private readonly Dictionary<string, ProfileView> _views = new();
    private Profile? _active;
    private bool _ungroupedExpanded = true;
    private bool _shutdownReady;

    public MainWindow()
    {
        InitializeComponent();
        _archive = new ProfileArchiveService(_store);
        InitFeatures();
    }

    // ================================================================ startup / shutdown

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try { _store.Load(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "AFKit", MessageBoxButton.OK, MessageBoxImage.Warning); }

        RefreshSidebar();
        OnStoreLoaded();
        InitSidebar();
        var last = _store.Profiles.FirstOrDefault(p => p.Id == _store.LastProfileId);
        if (IsGridShown)
        {
            SetActive(last is { IsPinned: true } ? last : _store.PinnedProfiles()[0]);
            ApplyLayout();
        }
        else if (last != null) await ActivateAsync(last);
        else ApplyLayout();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownReady) return;
        e.Cancel = true;

        // X / Alt+F4 → tray (everything keeps running). "Exit AFKit" in the tray really closes it.
        if (!_exitRequested && _store.CloseToTray)
        {
            HideToTray();
            return;
        }

        if (_inTray) RestoreFromTray();
        IsEnabled = false;
        ShowBusy(Loc.T("Main.SavingSessions"));

        foreach (var view in _views.Values.ToList())
        {
            await view.CloseAsync();
            view.Profile.IsLoaded = false;
        }
        _views.Clear();
        SaveStore();
        ShutdownFeatures();

        _shutdownReady = true;
        Close();
    }

    // ================================================================ sidebar

    private void RefreshSidebar()
    {
        var filter = SearchBox.Text.Trim();
        bool Matches(Profile p) => filter.Length == 0 || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

        var sections = new List<SidebarSection>();
        foreach (var group in _store.Groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var profiles = _store.ProfilesInGroup(group.Id).Where(Matches).ToList();
            if (filter.Length > 0 && profiles.Count == 0) continue;
            sections.Add(new SidebarSection { Group = group, Profiles = profiles, IsExpanded = group.IsExpanded || filter.Length > 0 });
        }

        var ungrouped = _store.ProfilesInGroup(null).Where(Matches).ToList();
        if (ungrouped.Count > 0 || (_store.Groups.Count == 0 && filter.Length == 0))
            sections.Add(new SidebarSection { Group = null, Profiles = ungrouped, IsExpanded = _ungroupedExpanded || filter.Length > 0 });

        Sections.ItemsSource = sections;
        RefreshTaskbar(); // V2: the battle window's order/names follow the sidebar

        var open = _store.Profiles.Count(p => p.IsLoaded);
        FooterText.Text = Loc.T("Main.Footer", _store.Profiles.Count, open);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshSidebar();

    private void OnSectionHeaderClicked(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SidebarSection section) return;
        if (section.Group != null) section.Group.IsExpanded = !section.Group.IsExpanded;
        else _ungroupedExpanded = !_ungroupedExpanded;
        SaveStore();
        RefreshSidebar();
    }

    /// <summary>Profiles in the order they appear in the sidebar (for Ctrl+1..9).</summary>
    private List<Profile> VisibleProfiles() =>
        (Sections.ItemsSource as IEnumerable<SidebarSection> ?? Enumerable.Empty<SidebarSection>())
        .Where(s => s.IsExpanded).SelectMany(s => s.Profiles).ToList();

    // ================================================================ sessions and layout

    /// <summary>Makes sure the profile has an open session (creates the ProfileView the first time).</summary>
    private ProfileView EnsureView(Profile profile)
    {
        if (_views.TryGetValue(profile.Id, out var view)) return view;

        view = new ProfileView(profile, _store) { Margin = ProfileView.Parked };
        view.Activated += (_, _) => OnViewActivated(profile);
        view.MaximizeRequested += (_, _) => ShowSingle(profile);
        view.UnpinRequested += (_, _) => Unpin(profile);
        view.PinTabRequested += (_, tab) => PinTab(profile, tab);
        view.ProfileMuteChanged += (_, _) => SaveStore();
        view.TabsReordered += (_, _) => SaveStore();
        view.MuteAllOffRequested += (_, tab) => UnmuteOnly(view, tab);
        view.GridDragMoved += (_, pt) => OnGridDragMoved(view, pt);
        view.GridDragEnded += (_, pt) => OnGridDragEnded(view, pt);
        view.AutoRecoverToggleRequested += (_, _) => ToggleAutoRecover();
        view.GameUpdated += (_, _) => RefreshTaskbar();
        view.TaskbarHit += (_, fx) => OnTaskbarHit(profile, view, fx);
        view.SessionCrashed += async (_, _) => await RestartCrashedSessionAsync(profile);
        _views[profile.Id] = view;
        ContentHost.Children.Add(view);
        profile.IsLoaded = true;
        RefreshSidebar();
        _ = view.InitializeAsync(); // several sessions can load in parallel (grid mode)
        return view;
    }

    private bool IsGridShown => _store.GridMode && _store.PinnedIds.Count > 0;

    private void SetActive(Profile? profile)
    {
        if (_active == profile) return;
        if (_active != null) _active.IsActive = false;
        _active = profile;
        if (profile != null)
        {
            profile.IsActive = true;
            _store.LastProfileId = profile.Id;
        }
        Title = profile == null ? "AFKit" : $"{profile.Name} — AFKit";
    }

    /// <summary>Positions the sessions: a grid of up to 4 pinned profiles, or one profile filling everything.</summary>
    private void ApplyLayout()
    {
        if (_store.GridMode && _store.PinnedIds.Count == 0) _store.GridMode = false;

        ContentHost.RowDefinitions.Clear();
        ContentHost.ColumnDefinitions.Clear();
        foreach (var splitter in _splitters) ContentHost.Children.Remove(splitter);
        _splitters.Clear();
        ContentHost.Margin = new Thickness(0);
        var onScreen = new HashSet<ProfileView>();
        // Whoever stays in the grid never leaves grid mode, not even for a moment: leaving and re-entering in the same step
        // hid the pinned tab (Light → invisible + Low memory) and showed it again right after,
        // which could leave the cell black.
        var staysInGrid = IsGridShown
            ? _store.PinnedProfiles().Where(p => _views.ContainsKey(p.Id)).Select(p => _views[p.Id]).ToHashSet()
            : new HashSet<ProfileView>();
        // Profiles off screen are "parked"; each tab decides whether it keeps drawing (background mode).
        foreach (var v in _views.Values)
        {
            v.Visibility = Visibility.Visible;
            v.Margin = ProfileView.Parked;
            Grid.SetRow(v, 0);
            Grid.SetColumn(v, 0);
            Grid.SetColumnSpan(v, 1);
            if (!staysInGrid.Contains(v)) v.SetGridMode(false, false);
        }

        if (IsGridShown)
        {
            var pinned = _store.PinnedProfiles();
            if (_active == null || !pinned.Contains(_active)) SetActive(pinned[0]);

            // 1 → full screen · 2 → side by side · 3 → 2 on top + 1 below · 4 → 2×2
            // Between cells there is a 14 px strip (Gap) with a draggable divider (GridSplitter).
            var twoCols = pinned.Count >= 2;
            var twoRows = pinned.Count >= 3;
            ContentHost.Margin = new Thickness(Gap);
            BuildGridDefinitions(twoCols, twoRows);

            for (var i = 0; i < pinned.Count; i++)
            {
                var view = EnsureView(pinned[i]);
                Grid.SetRow(view, (i / 2) * 2);
                Grid.SetColumn(view, (i % 2) * 2);
                if (pinned.Count == 3 && i == 2) Grid.SetColumnSpan(view, 3);
                view.Margin = new Thickness(0);
                view.EnsurePinnedTab();
                view.SetGridMode(true, pinned[i] == _active);
                onScreen.Add(view);
            }
            AddGridDividers(twoCols, twoRows, bottomSpansAll: pinned.Count == 3);
        }
        else if (_active != null)
        {
            var single = EnsureView(_active);
            single.Margin = new Thickness(0);
            onScreen.Add(single);
        }

        foreach (var v in _views.Values) v.SetOnScreen(onScreen.Contains(v));

        GridToggle.Foreground = (System.Windows.Media.Brush)FindResource(_store.GridMode ? "Accent" : "FgDim");
        GridToggle.ToolTip = Loc.T(_store.GridMode ? "Main.GridExit" : "Main.GridEnter");
        UpdateEmptyState();
    }

    // ---------------------------------------------------------------- grid dividers

    private readonly List<GridSplitter> _splitters = new();
    private const double Gap = 14;
    private const double MinCell = 220;

    private void BuildGridDefinitions(bool twoCols, bool twoRows)
    {
        var c = _store.GridColumnRatio;
        var r = _store.GridRowRatio;
        ContentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(twoCols ? c : 1, GridUnitType.Star), MinWidth = MinCell });
        if (twoCols)
        {
            ContentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            ContentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - c, GridUnitType.Star), MinWidth = MinCell });
        }
        ContentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(twoRows ? r : 1, GridUnitType.Star), MinHeight = 160 });
        if (twoRows)
        {
            ContentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap) });
            ContentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - r, GridUnitType.Star), MinHeight = 160 });
        }
    }

    private void AddGridDividers(bool twoCols, bool twoRows, bool bottomSpansAll)
    {
        var style = (Style)FindResource("GridDivider");
        if (twoCols)
        {
            var v = new GridSplitter { Style = style, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            Grid.SetColumn(v, 1);
            Grid.SetRow(v, 0);
            // With 3 profiles the bottom cell spans the full width: the vertical divider only covers the top row.
            Grid.SetRowSpan(v, twoRows && !bottomSpansAll ? 3 : 1);
            v.DragCompleted += (_, _) => SaveGridRatios();
            v.MouseDoubleClick += (_, _) => { _store.GridColumnRatio = 0.5; SaveStore(); ApplyLayout(); };
            _splitters.Add(v);
        }
        if (twoRows)
        {
            var h = new GridSplitter { Style = style, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            Grid.SetRow(h, 1);
            Grid.SetColumn(h, 0);
            Grid.SetColumnSpan(h, twoCols ? 3 : 1);
            h.DragCompleted += (_, _) => SaveGridRatios();
            h.MouseDoubleClick += (_, _) => { _store.GridRowRatio = 0.5; SaveStore(); ApplyLayout(); };
            _splitters.Add(h);
        }
        foreach (var s in _splitters) ContentHost.Children.Add(s);
    }

    private void SaveGridRatios()
    {
        var cols = ContentHost.ColumnDefinitions;
        if (cols.Count == 3)
        {
            var total = cols[0].ActualWidth + cols[2].ActualWidth;
            if (total > 0) _store.GridColumnRatio = Math.Clamp(cols[0].ActualWidth / total, 0.15, 0.85);
        }
        var rows = ContentHost.RowDefinitions;
        if (rows.Count == 3)
        {
            var total = rows[0].ActualHeight + rows[2].ActualHeight;
            if (total > 0) _store.GridRowRatio = Math.Clamp(rows[0].ActualHeight / total, 0.15, 0.85);
        }
        SaveStore();
    }

    /// <summary>Sidebar click / Ctrl+1..9.</summary>
    private Task ActivateAsync(Profile profile)
    {
        // In the grid, a pinned profile only gets focus; an unpinned one leaves the grid and opens on its own.
        if (_store.GridMode && !profile.IsPinned) _store.GridMode = false;
        SetActive(profile);
        ApplyLayout();
        if (_views.TryGetValue(profile.Id, out var view)) view.Focus();
        SaveStore();
        return Task.CompletedTask;
    }

    private void ShowSingle(Profile profile)
    {
        _store.GridMode = false;
        SetActive(profile);
        ApplyLayout();
        SaveStore();
    }

    private void OnViewActivated(Profile profile)
    {
        if (!IsGridShown || profile == _active) return;
        SetActive(profile);
        foreach (var p in _store.PinnedProfiles())
            if (_views.TryGetValue(p.Id, out var v)) v.SetGridMode(true, p == _active);
    }

    /// <summary>Pins a specific tab of the profile to the grid (swaps the tab if the profile was already in the grid).</summary>
    private void PinTab(Profile profile, BrowserTab? tab)
    {
        if (!profile.IsPinned && !_store.TogglePin(profile))
        {
            MessageBox.Show(Loc.T("Main.GridFull", ProfileStore.MaxPinned),
                "AFKit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var view = EnsureView(profile);
        if (tab != null) view.SetPinnedTab(tab);
        else view.EnsurePinnedTab();

        if (_store.GridMode) ApplyLayout();
        SaveStore();
        RefreshSidebar();
    }

    private void Unpin(Profile profile)
    {
        if (profile.IsPinned) _store.TogglePin(profile);
        if (_views.TryGetValue(profile.Id, out var view) && view.PinnedTab != null) view.SetPinnedTab(null);
        profile.GridTabIndex = null;
        ApplyLayout();
        SaveStore();
        RefreshSidebar();
    }

    private void ToggleGrid()
    {
        if (!_store.GridMode && _store.PinnedIds.Count == 0)
        {
            MessageBox.Show(Loc.T("Main.GridHowTo"), Loc.T("Main.GridTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _store.GridMode = !_store.GridMode;
        ApplyLayout();
        SaveStore();
    }

    private void OnGridToggleClicked(object sender, RoutedEventArgs e) => ToggleGrid();

    private void OnTogglePinMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;
        if (p.IsPinned) Unpin(p);
        else PinTab(p, null); // pins whichever tab is selected in the profile
    }

    /// <param name="reopenSoon">
    /// True when the session will be reopened right away (export):
    /// keeps the profile pinned and does not rearrange the layout in the middle of the operation.
    /// </param>
    private async Task CloseSessionAsync(Profile profile, bool reopenSoon = false)
    {
        if (!reopenSoon && profile.IsPinned) _store.TogglePin(profile);
        if (!_views.TryGetValue(profile.Id, out var view)) { SaveStore(); RefreshSidebar(); return; }

        _views.Remove(profile.Id);
        await view.CloseAsync();
        ContentHost.Children.Remove(view);
        profile.IsLoaded = false;

        if (_active == profile)
        {
            SetActive(null);
            if (!reopenSoon && IsGridShown) SetActive(_store.PinnedProfiles().FirstOrDefault());
        }
        if (!reopenSoon) ApplyLayout();
        SaveStore();
        RefreshSidebar();
        UpdateEmptyState();
    }

    /// <summary>Reopens a temporarily closed session, going back to the layout it had.</summary>
    private Task ReopenAsync(Profile profile, bool makeActive)
    {
        if (makeActive) SetActive(profile);
        ApplyLayout();
        if (!profile.IsLoaded && !IsGridShown && makeActive) EnsureView(profile).Visibility = Visibility.Visible;
        return Task.CompletedTask;
    }

    private void UpdateEmptyState()
    {
        var hasProfiles = _store.Profiles.Count > 0;
        EmptyState.Visibility = _active == null && !IsGridShown ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = Loc.T(hasProfiles ? "Main.EmptyNone" : "Main.EmptyWelcome");
        EmptyHint.Text = Loc.T(hasProfiles ? "Main.EmptyPickHint" : "Main.EmptyIsolatedHint");
        EmptyCreateButton.Visibility = hasProfiles ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveStore()
    {
        foreach (var view in _views.Values) view.SaveTabs();
        try { _store.Save(); }
        catch (Exception ex) { MessageBox.Show(Loc.T("Main.SaveError", ex.Message), "AFKit", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // ================================================================ profiles

    private static Profile? ProfileFrom(object sender) => (sender as FrameworkElement)?.DataContext as Profile;

    private async void OnNewProfileClicked(object sender, RoutedEventArgs e) => await CreateProfileAsync(null);

    private async void OnNewProfileInGroupClicked(object sender, RoutedEventArgs e)
    {
        var section = (sender as FrameworkElement)?.DataContext as SidebarSection;
        await CreateProfileAsync(section?.Group?.Id);
    }

    private async Task CreateProfileAsync(string? groupId)
    {
        var profile = new Profile
        {
            Name = Loc.T("Main.DefaultProfileName", _store.Profiles.Count + 1),
            GroupId = groupId,
            Order = _store.NextOrder(groupId),
            Color = ProfileEditorWindow.Palette[_store.Profiles.Count % ProfileEditorWindow.Palette.Length]
        };
        var dialog = new ProfileEditorWindow(profile, _store.Groups, isNew: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _store.Profiles.Add(profile);
        SaveStore();
        RefreshSidebar();
        await ActivateAsync(profile);
    }

    private async void OnOpenProfileMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is { } p) await ActivateAsync(p);
    }

    private void OnEditProfileMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;
        var oldGroup = p.GroupId;
        var dialog = new ProfileEditorWindow(p, _store.Groups, isNew: false) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (p.GroupId != oldGroup) _store.MoveProfile(p, p.GroupId, int.MaxValue); // goes to the end of the new group

        SaveStore();
        RefreshSidebar();
        if (p == _active) Title = $"{p.Name} — AFKit";
    }

    private void ToggleProfileMute(Profile profile)
    {
        profile.IsMuted = !profile.IsMuted; // an open session applies it right away; a closed one applies it on open
        SaveStore();
    }

    private void OnToggleProfileMuteMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is { } p) ToggleProfileMute(p);
    }

    private async void OnCloseSessionMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is { } p) await CloseSessionAsync(p);
    }

    private void OnOpenDataFolderMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;
        var folder = _store.GetUserDataFolder(p);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private async void OnDeleteProfileMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;
        var confirm = MessageBox.Show(
            Loc.T("Main.DeleteProfileText", p.Name),
            Loc.T("Main.DeleteProfileTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        await CloseSessionAsync(p);
        _store.Profiles.Remove(p);
        if (_store.LastProfileId == p.Id) _store.LastProfileId = null;
        SaveStore();
        RefreshSidebar();
        UpdateEmptyState();
        await _store.DeleteUserDataAsync(p);
    }

    // ================================================================ backup

    private async void OnExportProfileMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Main.ExportTitle"),
            FileName = $"{MakeSafeFileName(p.Name)}-{DateTime.Now:yyyyMMdd}{ProfileArchiveService.Extension}",
            Filter = $"{Loc.T("Main.ProfileFileType")} (*{ProfileArchiveService.Extension})|*{ProfileArchiveService.Extension}"
        };
        if (dialog.ShowDialog(this) != true) return;

        var wasLoaded = p.IsLoaded;
        var wasActive = p == _active;
        if (wasLoaded)
        {
            var ok = MessageBox.Show(Loc.T("Main.ExportNeedsClose"),
                Loc.T("Main.ExportTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (ok != MessageBoxResult.OK) return;
        }

        ShowBusy(Loc.T("Main.Exporting", p.Name));
        try
        {
            if (wasLoaded) await CloseSessionAsync(p, reopenSoon: true);
            var progress = new Progress<string>(file => BusyDetail.Text = file);
            var skipped = await _archive.ExportAsync(p, dialog.FileName, progress);
            HideBusy();

            var size = new FileInfo(dialog.FileName).Length / 1024.0 / 1024.0;
            var note = skipped > 0 ? "\n\n" + Loc.T("Main.ExportSkipped", skipped) : "";
            MessageBox.Show(Loc.T("Main.ExportDone", size.ToString("0.0")) + note,
                Loc.T("Main.ExportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            HideBusy();
            MessageBox.Show(Loc.T("Main.ExportFailed", ex.Message), Loc.T("Main.ExportTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (wasActive || (wasLoaded && p.IsPinned)) await ReopenAsync(p, wasActive);
        }
    }

    private async void OnImportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Main.ImportTitle"),
            Filter = $"{Loc.T("Main.ProfileFileType")} (*{ProfileArchiveService.Extension};*.deckprofile)|*{ProfileArchiveService.Extension};*.deckprofile|Zip (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        ShowBusy(Loc.T("Main.Importing"));
        try
        {
            var profile = await _archive.ImportAsync(dialog.FileName);
            profile.Order = _store.NextOrder(profile.GroupId);
            _store.Profiles.Add(profile);
            SaveStore();
            HideBusy();
            RefreshSidebar();
            await ActivateAsync(profile);
        }
        catch (Exception ex)
        {
            HideBusy();
            MessageBox.Show(Loc.T("Main.ImportFailed", ex.Message), Loc.T("Main.ImportTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private void ShowBusy(string title)
    {
        BusyTitle.Text = title;
        BusyDetail.Text = "";
        BusyOverlay.Visibility = Visibility.Visible;
        // WebView2 is a native window and sits on top of any WPF element; hide it during the operation.
        ContentHost.Visibility = Visibility.Hidden;
    }

    private void HideBusy()
    {
        BusyOverlay.Visibility = Visibility.Collapsed;
        ContentHost.Visibility = Visibility.Visible;
    }

    // ================================================================ groups

    private void OnNewGroupClicked(object sender, RoutedEventArgs e)
    {
        var group = new ProfileGroup { Color = ProfileEditorWindow.Palette[_store.Groups.Count % ProfileEditorWindow.Palette.Length] };
        if (new GroupEditorWindow(group, isNew: true) { Owner = this }.ShowDialog() != true) return;
        _store.Groups.Add(group);
        SaveStore();
        RefreshSidebar();
    }

    private void OnEditGroupClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SidebarSection { Group: { } group }) return;
        if (new GroupEditorWindow(group, isNew: false) { Owner = this }.ShowDialog() != true) return;
        SaveStore();
        RefreshSidebar();
    }

    private void OnDeleteGroupClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SidebarSection { Group: { } group }) return;
        var confirm = MessageBox.Show(Loc.T("Main.DeleteGroupText", group.Name),
            Loc.T("Main.DeleteGroupTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var p in _store.Profiles.Where(p => p.GroupId == group.Id)) p.GroupId = null;
        _store.Groups.Remove(group);
        SaveStore();
        RefreshSidebar();
    }

    // ================================================================ global shortcuts

    private async void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            e.Handled = true;
            await CreateProfileAsync(null);
            return;
        }

        if (mods == (ModifierKeys.Control | ModifierKeys.Alt) && (e.Key == Key.System ? e.SystemKey : e.Key) == Key.M)
        {
            e.Handled = true;
            ToggleMuteAll();
            return;
        }

        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.M && _active != null)
        {
            e.Handled = true;
            ToggleProfileMute(_active);
            return;
        }

        if (mods == ModifierKeys.Control && e.Key == Key.G)
        {
            e.Handled = true;
            ToggleGrid();
            return;
        }

        if (mods == ModifierKeys.Control && e.Key == Key.B)
        {
            e.Handled = true;
            ToggleSidebar(); // sidebar: show ↔ hide (icon strip)
            return;
        }

        // Ctrl+1..9 → switch profile in sidebar order.
        if (mods == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            var list = VisibleProfiles();
            var index = e.Key - Key.D1;
            if (index < list.Count)
            {
                e.Handled = true;
                await ActivateAsync(list[index]);
            }
        }
    }
}
