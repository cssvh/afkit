using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFKit.Dialogs;
using AFKit.Models;
using AFKit.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AFKit.Controls;

/// <summary>
/// A browser "session" for one profile: its own WebView2 environment (data folder) and several tabs.
/// </summary>
public partial class ProfileView : UserControl
{
    private readonly ProfileStore _store;
    private readonly ObservableCollection<BrowserTab> _tabs = new();
    private CoreWebView2Environment? _env;
    private BrowserTab? _selected;
    private bool _closing;
    private bool _restoring; // restoring tabs: do not steal focus (important in grid mode)
    private bool _crashReported;

    public Profile Profile { get; }

    /// <summary>
    /// Position of a whole profile that is not in view (used by MainWindow): outside the window area,
    /// but with the same size. It goes UP, while parked tabs (<see cref="ParkedPage"/>) go
    /// LEFT: that way the offsets do not add up. With -20000 at both levels, a tab could end up at
    /// -40000 DIP (-80000 px at 200% scale), outside the 16-bit range Windows uses in several window
    /// messages (WM_MOVE etc.) — one of the suspects for the black cells in the grid.
    /// </summary>
    public static readonly Thickness Parked = new(0, -12000, 0, 12000);

    /// <summary>
    /// Position of a page that is not in view: outside the window area, but with the same size and still
    /// "visible" to Chromium. That way games and timers keep running at full speed
    /// (a page hidden with Visibility.Collapsed would be paused/throttled by the browser).
    /// A child window outside its parent's area never shows up, so being larger than the window width is enough.
    /// </summary>
    private static readonly Thickness ParkedPage = new(-12000, 0, 12000, 0);

    /// <summary>Chromium flags that turn off power saving for background pages.</summary>
    private const string KeepAliveArguments =
        "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows" +
        // Lives may start playing with the player's sound on (muting is up to the tab, in AFKit).
        " --autoplay-policy=no-user-gesture-required";

    private bool _inGrid;
    private bool _pinWhenReady;

    /// <summary>This profile's tab that shows up in the grid (null = profile is not in the grid).</summary>
    public BrowserTab? PinnedTab { get; private set; }

    /// <summary>The tab in view: the one pinned to the grid, or the selected one in normal mode.</summary>
    private BrowserTab? ShownTab => _inGrid ? PinnedTab ?? _selected : _selected;

    /// <summary>The user clicked/focused this profile.</summary>
    public event EventHandler? Activated;
    /// <summary>The grid header's "open alone" button.</summary>
    public event EventHandler? MaximizeRequested;
    /// <summary>"Remove from grid" button (grid header, tab menu, or the pinned tab was closed).</summary>
    public event EventHandler? UnpinRequested;
    /// <summary>The user asked to pin a tab to the grid (MainWindow decides whether it fits).</summary>
    public event EventHandler<BrowserTab>? PinTabRequested;

    public ProfileView(Profile profile, ProfileStore store)
    {
        InitializeComponent();
        Profile = profile;
        _store = store;
        TabStrip.ItemsSource = _tabs;
        _headerTimer.Tick += (_, _) => UpdateHeaderTab();
        InitGridDrag();
        Profile.PropertyChanged += (_, e) =>
        {
            UpdateHeader();
            if (e.PropertyName == nameof(Models.Profile.Color)) ApplyGridFrame();
            if (e.PropertyName == nameof(Models.Profile.IsMuted))
            {
                foreach (var t in _tabs) ApplyMute(t);
                UpdateHeaderTab();
            }
        };
        UpdateHeader();
    }

    // ------------------------------------------------------------------ grid mode

    /// <summary>
    /// Grid mode: shows only the pinned tab, with a compact header (no tab bar or address bar).
    /// The profile's other tabs stay open and running, parked off screen.
    /// </summary>
    public void SetGridMode(bool inGrid, bool focused)
    {
        _inGrid = inGrid;
        GridHeader.Visibility = inGrid ? Visibility.Visible : Visibility.Collapsed;
        TabsRow.Visibility = inGrid ? Visibility.Collapsed : Visibility.Visible;
        NavRow.Visibility = inGrid ? Visibility.Collapsed : Visibility.Visible;
        _gridFocused = focused;
        ApplyGridFrame();
        UpdatePageVisibility();
        UpdateHeaderTab();
        UpdateZoomUi();
        if (inGrid) _headerTimer.Start(); else _headerTimer.Stop();
    }

    /// <summary>Defines which tab goes to the grid (null = remove from the grid).</summary>
    public void SetPinnedTab(BrowserTab? tab)
    {
        _pinWhenReady = false;
        if (PinnedTab != null) PinnedTab.IsPinned = false;
        PinnedTab = tab;
        if (tab != null) tab.IsPinned = true;
        UpdateHeaderTab();
        UpdatePageVisibility();
    }

    /// <summary>Makes sure there is a pinned tab (the selected one, or the one saved last time once the tabs finish opening).</summary>
    public void EnsurePinnedTab()
    {
        if (PinnedTab != null) return;
        if (_tabs.Count == 0 || _restoring) { _pinWhenReady = true; return; }
        SetPinnedTab(_selected ?? _tabs[0]);
    }

    private bool _onScreen;

    /// <summary>MainWindow says whether this profile is on screen (alone or in a grid cell).</summary>
    public void SetOnScreen(bool onScreen)
    {
        if (_onScreen == onScreen) return;
        _onScreen = onScreen;
        UpdatePageVisibility();
    }

    /// <summary>
    /// The tab in view fills the visible area. The others follow each one's background mode:
    /// Active = keeps drawing off screen; Light = hidden (scripts keep running, no drawing);
    /// Suspended = hidden and frozen.
    /// </summary>
    private void UpdatePageVisibility()
    {
        var shown = ShownTab;
        foreach (var tab in _tabs)
        {
            if (_onScreen && tab == shown)
            {
                // Order matters: normal memory and "wake up" BEFORE becoming visible again. With the Low level
                // Chromium discards the page's drawing resources; if it reappears before that is undone,
                // the native window can stay black until something forces a new frame.
                SetMemoryLevel(tab, low: false);
                ResumeIfSuspended(tab);
                tab.WebView.Margin = new Thickness(0);
                tab.WebView.Visibility = Visibility.Visible;
                if (tab != _nudgedTab && tab.WebView.CoreWebView2 != null)
                {
                    _nudgedTab = tab;
                    NudgeRepaint(tab);
                }
                continue;
            }

            tab.WebView.Margin = ParkedPage;
            if (tab.WebView.CoreWebView2 == null)
            {
                // Still initializing: stays visible (off screen) until WebView2 finishes creating the page.
                tab.WebView.Visibility = Visibility.Visible;
                continue;
            }
            switch (tab.Mode)
            {
                case BackgroundMode.Active:
                    tab.WebView.Visibility = Visibility.Visible;
                    SetMemoryLevel(tab, low: false);
                    break;
                case BackgroundMode.Suspended:
                    tab.WebView.Visibility = Visibility.Collapsed;
                    SetMemoryLevel(tab, low: true);
                    SuspendLater(tab);
                    break;
                default:
                    tab.WebView.Visibility = Visibility.Collapsed;
                    SetMemoryLevel(tab, low: true);
                    break;
            }
        }
        // Nobody in view: the next time a tab appears (even the same one) it gets the repaint "nudge".
        if (!_onScreen || shown == null) _nudgedTab = null;
        UpdateGridOverlays(); // did the grid cell's tab change? turns immersive mode and the watermarks on/off
    }

    /// <summary>Last tab that appeared on screen and already got the repaint nudge.</summary>
    private BrowserTab? _nudgedTab;

    private bool IsShownOnScreen(BrowserTab tab) => !_closing && _onScreen && tab == ShownTab && _tabs.Contains(tab);

    /// <summary>
    /// Forces WebView2 to draw a new frame after coming back on screen.
    /// WebView2 in WPF is a native window (HwndHost) drawn by Chromium's GPU process; after being
    /// parked off screen, hidden (Light mode) or with the AFKit window off screen (tray),
    /// it sometimes comes back with no content — black until the window is resized. With 4 profiles in the grid
    /// (4 browsers, each with its own GPU process) this shows up much more. The "nudge" imitates that
    /// resize: it shrinks the page by 1 px for a moment and gives the size back, which forces
    /// Chromium to rebuild the drawing surface. It does not flicker (it is just 1 px on the right edge).
    /// </summary>
    private void NudgeRepaint(BrowserTab tab)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            // Wait for the layout to put the native window in its new place.
            await Task.Delay(80);
            if (!IsShownOnScreen(tab) || tab.WebView.CoreWebView2 == null) return;
            tab.WebView.Margin = new Thickness(0, 0, 1, 0);
            await Task.Delay(80);
            if (IsShownOnScreen(tab)) tab.WebView.Margin = new Thickness(0);
        }, DispatcherPriority.Background);
    }

    /// <summary>Called by MainWindow when the window comes back (e.g. from the tray): repaints the page in view.</summary>
    public void NudgeRepaint()
    {
        if (ShownTab is { } tab && IsShownOnScreen(tab)) NudgeRepaint(tab);
    }

    private static void ResumeIfSuspended(BrowserTab tab)
    {
        try
        {
            // In theory becoming visible already lifts the suspension; we call it explicitly so we do not depend on that.
            if (tab.WebView.CoreWebView2 is { IsSuspended: true } core) core.Resume();
        }
        catch (Exception) { /* old runtime / page closing: ignore */ }
    }

    private static void SetMemoryLevel(BrowserTab tab, bool low)
    {
        try
        {
            if (tab.WebView.CoreWebView2 is { } core)
                core.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch (Exception) { /* old runtime without support: ignore */ }
    }

    /// <summary>Suspends after WebView2 registered that it became invisible (suspending requires that).</summary>
    private void SuspendLater(BrowserTab tab)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(1500);
            var core = tab.WebView.CoreWebView2;
            if (core == null || _closing || !_tabs.Contains(tab) || tab.Mode != BackgroundMode.Suspended) return;
            if (tab.WebView.Visibility == Visibility.Visible || core.IsSuspended) return;
            try { await core.TrySuspendAsync(); }
            catch (Exception) { /* busy page (e.g. playing audio): it just stays hidden */ }
        }, DispatcherPriority.Background);
    }

    public void SetBackgroundMode(BrowserTab tab, BackgroundMode mode)
    {
        tab.Mode = mode;
        UpdatePageVisibility();
    }

    private void UpdateHeaderTab()
    {
        var recovering = PinnedTab?.IsRecovering == true;
        var loading = PinnedTab == null || PinnedTab.IsLoading || PinnedTab.WebView.CoreWebView2 == null;
        HeaderTab.Text = recovering
            ? PinnedTab!.RecoveryStatus!
            : loading ? Loc.T("PV.LoadingDots") : $"{OpenedAgo()} · {PinnedTab!.Title}";
        HeaderTab.Foreground = (System.Windows.Media.Brush)FindResource(recovering ? "Warning" : "FgDim");
        HeaderDot.Fill = recovering ? (System.Windows.Media.Brush)FindResource("Warning")
                       : loading ? (System.Windows.Media.Brush)FindResource("FgDim")
                       : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x7A));
        HeaderDot.ToolTip = Loc.T(recovering ? "PV.StatusRecovering" : loading ? "PV.StatusLoading" : "PV.StatusOk");
        HeaderReload.Visibility = PinnedTab?.HasAutoReload == true ? Visibility.Visible : Visibility.Collapsed;
        HeaderReload.ToolTip = PinnedTab?.AutoReloadTooltip;
        var muted = PinnedTab?.EffectiveMuted == true || (PinnedTab == null && Profile.IsMuted);
        HeaderMuteButton.Content = muted ? "" : "";
        HeaderMuteButton.ToolTip = Loc.T(muted ? "PV.UnmuteTip" : "PV.MuteTip");
        HeaderMuteButton.Foreground = (System.Windows.Media.Brush)FindResource(muted ? "Danger" : "FgDim");

        var economy = ShownTab?.Economy;
        HeaderEconomyButton.Visibility = _inGrid && economy != null ? Visibility.Visible : Visibility.Collapsed;
        HeaderEconomyButton.ToolTip = Loc.T(economy == true ? "PV.EconomyOff" : "PV.EconomyOn");
        HeaderEconomyButton.Foreground = (System.Windows.Media.Brush)FindResource(economy == true ? "Accent" : "FgDim");
    }

    /// <summary>V2: asks the game page to turn its Economy Mode on/off (the page answers with its real state).</summary>
    private void OnHeaderEconomyClicked(object sender, RoutedEventArgs e) => SetEconomy(ShownTab, ShownTab?.Economy != true);

    // ------------------------------------------------------------------ sound

    /// <summary>The user turned the whole profile's sound on through a tab (MainWindow saves).</summary>
    public event EventHandler? ProfileMuteChanged;
    /// <summary>The user turned a tab's sound on with "Mute everything" on (only that tab should play again).</summary>
    public event EventHandler<BrowserTab>? MuteAllOffRequested;
    /// <summary>This profile's browser process crashed and there are tabs with "reload if it fails" (MainWindow restarts the session).</summary>
    public event EventHandler? SessionCrashed;

    /// <summary>Actual sound = muted by the tab itself OR profile muted.</summary>
    private void ApplyMute(BrowserTab tab)
    {
        tab.IsProfileMuted = Profile.IsMuted || _store.MuteAll;
        if (tab.WebView.CoreWebView2 is { } core) core.IsMuted = tab.EffectiveMuted;
        if (tab == PinnedTab) UpdateHeaderTab();
    }

    /// <summary>Reapplies the sound to all tabs (called when "Mute everything" changes).</summary>
    public void RefreshMute()
    {
        foreach (var t in _tabs) ApplyMute(t);
        UpdateHeaderTab();
    }

    public void ToggleMute(BrowserTab? tab)
    {
        if (tab == null) return;
        if (!tab.EffectiveMuted)
        {
            tab.IsMuted = true;
            ApplyMute(tab);
            return;
        }
        // From here on: turn the sound on for THIS tab only. The others stay silent.
        if (_store.MuteAll)
        {
            // "Mute everything" is on: MainWindow turns the global switch off and mutes the other tabs one by one.
            MuteAllOffRequested?.Invoke(this, tab);
            return;
        }
        if (Profile.IsMuted)
        {
            // Profile muted: swap the profile's mute for a mute on each tab, except this one.
            KeepOnlyAudible(tab);
            ProfileMuteChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        tab.IsMuted = false;
        ApplyMute(tab);
    }

    /// <summary>
    /// Leaves only the given tab with sound (null = none): the others are muted one by one.
    /// With a tab given, the profile's mute is lifted (otherwise it would stay silent).
    /// </summary>
    public void KeepOnlyAudible(BrowserTab? keep)
    {
        foreach (var t in _tabs) t.IsMuted = t != keep;
        if (keep != null && Profile.IsMuted) Profile.IsMuted = false;
        RefreshMute();
    }

    private void OnAudioButtonClicked(object sender, RoutedEventArgs e)
    {
        ToggleMute(TabFrom(sender));
        e.Handled = true; // do not select the tab when clicking the speaker
    }

    private void OnToggleMuteMenu(object sender, RoutedEventArgs e) => ToggleMute(TabFrom(sender));
    private void OnHeaderMuteClicked(object sender, RoutedEventArgs e) => ToggleMute(PinnedTab);

    private void UpdateHeader()
    {
        HeaderName.Text = Profile.Name;
        HeaderUsage.Text = Profile.MemoryText;
        HeaderUsage.ToolTip = string.IsNullOrEmpty(Profile.UsageText) ? null : Profile.UsageText;
    }

    private DateTime _openedAt = DateTime.Now;
    private readonly DispatcherTimer _headerTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    private string OpenedAgo()
    {
        var t = DateTime.Now - _openedAt;
        return t.TotalSeconds < 60 ? Loc.T("PV.OpenedSeconds", $"{t.TotalSeconds:0}")
             : t.TotalMinutes < 60 ? Loc.T("PV.OpenedMinutes", $"{t.TotalMinutes:0}")
             : Loc.T("PV.OpenedHours", $"{t.TotalHours:0}");
    }

    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private void OnFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsKeyboardFocusWithin) Activated?.Invoke(this, EventArgs.Empty);
    }

    private void OnMaximizeClicked(object sender, RoutedEventArgs e) => MaximizeRequested?.Invoke(this, EventArgs.Empty);
    private void OnUnpinClicked(object sender, RoutedEventArgs e) => UnpinRequested?.Invoke(this, EventArgs.Empty);

    public async Task InitializeAsync()
    {
        _openedAt = DateTime.Now;
        ShowStatus(Loc.T("PV.OpeningProfile"));
        try
        {
            var options = new CoreWebView2EnvironmentOptions(KeepAliveArguments)
            {
                // Per-profile extensions ("Extensions…" dialog); has to be on when the environment is created.
                AreBrowserExtensionsEnabled = true
            };
            _env = await CoreWebView2Environment.CreateAsync(null, _store.GetUserDataFolder(Profile), options);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.T("PV.StartFailed", ex.Message));
            return;
        }

        HideStatus();

        var urls = Profile.SavedTabs.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        if (urls.Count == 0) urls.Add(Profile.StartUrl);

        _restoring = true;
        try
        {
            // The first tab is born blank: the profile's extensions are applied before it navigates.
            // Saved options of each tab (the lives' sound/quality go in before the page is born).
            TabSetting? Saved(int i) => Profile.TabOptions.FirstOrDefault(o => o.Index == i);
            // The tab is born muted if it was muted (lives may start playing with sound on their own).
            bool MutedAt(int i) => Profile.MutedTabIndexes.Contains(i);
            var first = await CreateTabAsync(url: null, select: true, restore: Saved(0), startMuted: MutedAt(0));
            if (first != null)
            {
                await ApplyExtensionsOnStartAsync(first);
                if (!_closing && _tabs.Contains(first)) Navigate(first, urls[0]);
            }
            var index = first != null ? 1 : 0;
            foreach (var url in urls.Skip(index))
            {
                await CreateTabAsync(url, select: _tabs.Count == 0, restore: Saved(index), startMuted: MutedAt(index));
                index++;
            }
        }
        finally { _restoring = false; }

        foreach (var opt in Profile.TabOptions.Where(o => o.Index >= 0 && o.Index < _tabs.Count))
        {
            SetReloadInterval(_tabs[opt.Index], opt.ReloadMinutes);
            _tabs[opt.Index].Mode = opt.Mode;
            if (opt.Zoom > 0 && Math.Abs(opt.Zoom - 1) > 0.001) _tabs[opt.Index].WebView.ZoomFactor = opt.Zoom;
        }
        UpdatePageVisibility();

        foreach (var i in Profile.MutedTabIndexes.Where(i => i >= 0 && i < _tabs.Count))
        {
            _tabs[i].IsMuted = true;
            ApplyMute(_tabs[i]);
        }

        if (_pinWhenReady && _tabs.Count > 0)
        {
            var i = Profile.GridTabIndex ?? -1;
            SetPinnedTab(i >= 0 && i < _tabs.Count ? _tabs[i] : _tabs[0]);
        }
    }

    // ------------------------------------------------------------------ tabs

    public async Task<BrowserTab?> CreateTabAsync(string? url, bool select = true, TabSetting? restore = null, bool startMuted = false)
    {
        if (_env == null || _closing) return null;

        var webView = new WebView2 { Margin = ParkedPage, DefaultBackgroundColor = System.Drawing.Color.White };
        var tab = new BrowserTab(webView);
        WebHost.Children.Add(webView);
        _tabs.Add(tab);
        if (select) Select(tab);

        try
        {
            await webView.EnsureCoreWebView2Async(_env);
        }
        catch (Exception ex)
        {
            _tabs.Remove(tab);
            WebHost.Children.Remove(webView);
            webView.Dispose();
            MessageBox.Show(ex.Message, Loc.T("PV.CreateTabError"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        HookEvents(tab);
        await AttachTaskbarProbeAsync(tab); // V2: before navigating, to catch the game's connection from the start
        await AttachGridOverlayAsync(tab);  // grid ↔ immersive mode + watermarks
        await AttachLiveStateAsync(tab, restore); // Twitch/Kick: player sound and quality as they were
        if (startMuted) tab.IsMuted = true;
        ApplyMute(tab); // new tabs inherit the profile's mute
        UpdatePageVisibility();
        if (url != null) Navigate(tab, url);
        return tab;
    }

    private void HookEvents(BrowserTab tab)
    {
        var core = tab.WebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = true;

        core.SourceChanged += (_, _) =>
        {
            tab.Url = core.Source;
            if (tab == _selected && !AddressBar.IsKeyboardFocusWithin) AddressBar.Text = DisplayUrl(core.Source);
            if (tab == _selected) UpdateAddressIcon(core.Source);
        };
        core.DocumentTitleChanged += (_, _) =>
        {
            tab.Title = core.DocumentTitle;
            if (tab == PinnedTab) UpdateHeaderTab();
        };
        core.IsDocumentPlayingAudioChanged += (_, _) => tab.IsPlayingAudio = core.IsDocumentPlayingAudio;
        core.NavigationStarting += (_, _) =>
        {
            tab.IsLoading = true;
            if (tab == PinnedTab) UpdateHeaderTab();
        };
        tab.WebView.ZoomFactorChanged += (_, _) =>
        {
            tab.Zoom = tab.WebView.ZoomFactor;
            UpdateZoomUi();
        };
        core.NavigationCompleted += (_, e) =>
        {
            tab.IsLoading = false;
            UpdateNavButtons();
            OnNavigationResult(tab, e);
            if (tab == PinnedTab) UpdateHeaderTab();
        };
        core.HistoryChanged += (_, _) => UpdateNavButtons();
        core.FaviconChanged += async (_, _) => tab.Favicon = await LoadFaviconAsync(core);
        core.WindowCloseRequested += (_, _) => CloseTab(tab);
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                // The profile's whole browser crashed: only restarting the session helps.
                if (!_closing && !_crashReported && _store.AutoRecover)
                {
                    _crashReported = true;
                    SessionCrashed?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                var reason = Loc.T(e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
                    ? "PV.PageHung" : "PV.PageCrashed");
                HandleFailure(tab, reason, firstDelay: TimeSpan.FromSeconds(3));
            }
        };

        // window.open / target=_blank / login popups open as a new tab in the same profile.
        core.NewWindowRequested += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                var newTab = await CreateTabAsync(url: null, select: true);
                if (newTab != null)
                {
                    e.NewWindow = newTab.WebView.CoreWebView2;
                    e.Handled = true;
                }
            }
            finally { deferral.Complete(); }
        };

        core.BasicAuthenticationRequested += OnBasicAuthenticationRequested;
    }

    private void OnBasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        // The dialog is asynchronous relative to the event: we show it after the handler returns.
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var dialog = new CredentialDialog(e.Uri, e.Challenge)
                {
                    Owner = Window.GetWindow(this)
                };
                if (dialog.ShowDialog() == true)
                {
                    e.Response.UserName = dialog.UserName;
                    e.Response.Password = dialog.Password;
                }
                else
                {
                    e.Cancel = true;
                }
            }
            finally { deferral.Complete(); }
        });
    }

    private static async Task<BitmapImage?> LoadFaviconAsync(CoreWebView2 core)
    {
        try
        {
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream == null || stream.Length == 0) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    public void Select(BrowserTab tab)
    {
        if (_selected != null) _selected.IsSelected = false;
        _selected = tab;
        tab.IsSelected = true;
        UpdatePageVisibility();
        AddressBar.Text = DisplayUrl(tab.Url);
        UpdateAddressIcon(tab.Url);
        UpdateNavButtons();
        UpdateZoomUi();
        if (!_restoring && tab == ShownTab) tab.WebView.Focus();
    }

    public void CloseTab(BrowserTab tab)
    {
        if (_closing) return;
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;

        _tabs.RemoveAt(index);
        tab.ReloadTimer?.Stop();
        WebHost.Children.Remove(tab.WebView);
        tab.WebView.Dispose();

        if (tab == PinnedTab)
        {
            SetPinnedTab(null);
            UnpinRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_tabs.Count == 0)
        {
            _selected = null;
            _ = CreateTabAsync(Profile.StartUrl);
            return;
        }
        if (tab == _selected)
        {
            _selected = null;
            Select(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
    }

    private void SelectRelative(int delta)
    {
        if (_selected == null || _tabs.Count < 2) return;
        var i = (_tabs.IndexOf(_selected) + delta + _tabs.Count) % _tabs.Count;
        Select(_tabs[i]);
    }

    // ------------------------------------------------------------------ navigation

    private void Navigate(BrowserTab tab, string input)
    {
        var url = NormalizeUrl(input);
        try { tab.WebView.CoreWebView2.Navigate(url); }
        catch (ArgumentException) { tab.WebView.CoreWebView2.Navigate(SearchUrl(input)); }
    }

    public static string NormalizeUrl(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return "about:blank";
        if (Uri.TryCreate(input, UriKind.Absolute, out var abs) &&
            (abs.Scheme is "http" or "https" or "file" or "about" or "edge"))
            return abs.ToString();

        var looksLikeHost = !input.Contains(' ') &&
                            (input.Contains('.') || input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
        if (looksLikeHost && Uri.TryCreate("https://" + input, UriKind.Absolute, out var withScheme))
            return input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "http://" + input : withScheme.ToString();

        return SearchUrl(input);
    }

    private static string SearchUrl(string q) => "https://www.google.com/search?q=" + Uri.EscapeDataString(q);

    private static string DisplayUrl(string? url) => url is null or "about:blank" ? "" : url;

    private void UpdateNavButtons()
    {
        var core = _selected?.WebView.CoreWebView2;
        BackButton.IsEnabled = core?.CanGoBack ?? false;
        ForwardButton.IsEnabled = core?.CanGoForward ?? false;
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Stores the open URLs in the profile (to restore next time).</summary>
    public void SaveTabs()
    {
        var kept = _tabs.Where(t => !string.IsNullOrWhiteSpace(t.Url) && t.Url != "about:blank").ToList();
        if (kept.Count == 0) return;
        Profile.SavedTabs = kept.Select(t => t.Url).ToList();
        var pinned = PinnedTab == null ? -1 : kept.IndexOf(PinnedTab);
        Profile.GridTabIndex = pinned >= 0 ? pinned : null;
        Profile.MutedTabIndexes = kept.Select((t, i) => (t, i)).Where(x => x.t.IsMuted).Select(x => x.i).ToList();
        Profile.TabOptions = kept.Select((t, i) => (t, i))
            .Where(x => x.t.HasAutoReload || x.t.Mode != BackgroundMode.Light || Math.Abs(x.t.Zoom - 1) > 0.001 ||
                        x.t.LiveMuted != null || x.t.LiveQuality != null)
            .Select(x => new TabSetting
            {
                Index = x.i, ReloadMinutes = x.t.ReloadMinutes, Mode = x.t.Mode, Zoom = x.t.Zoom,
                LiveMuted = x.t.LiveMuted, LiveVolume = x.t.LiveVolume, LiveQuality = x.t.LiveQuality
            })
            .ToList();
    }

    /// <summary>Closes all tabs and waits for the browser process to end (releases the profile's files).</summary>
    public async Task CloseAsync()
    {
        if (_closing) return;
        SaveTabs();
        _closing = true;

        var exited = new TaskCompletionSource();
        if (_env != null) _env.BrowserProcessExited += (_, _) => exited.TrySetResult();

        foreach (var tab in _tabs.ToList())
        {
            tab.ReloadTimer?.Stop();
            WebHost.Children.Remove(tab.WebView);
            tab.WebView.Dispose();
        }
        _tabs.Clear();
        _selected = null;

        if (_env != null)
            await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(6)));
        _env = null;
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;

    // ------------------------------------------------------------------ UI handlers

    private void OnTabClicked(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BrowserTab tab) Select(tab);
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ((FrameworkElement)sender).DataContext is BrowserTab tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    private void OnCloseTabClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BrowserTab tab) CloseTab(tab);
        e.Handled = true;
    }

    private async void OnNewTabClicked(object sender, RoutedEventArgs e) => await NewTabAndFocusAddressAsync();

    private async Task NewTabAndFocusAddressAsync()
    {
        await CreateTabAsync(Profile.StartUrl);
        AddressBar.Focus();
        AddressBar.SelectAll();
    }

    private void OnTabsMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TabsScroll.ScrollToHorizontalOffset(TabsScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnBackClicked(object sender, RoutedEventArgs e) => _selected?.WebView.CoreWebView2?.GoBack();
    private void OnForwardClicked(object sender, RoutedEventArgs e) => _selected?.WebView.CoreWebView2?.GoForward();
    private void OnReloadClicked(object sender, RoutedEventArgs e) => ShownTab?.WebView.CoreWebView2?.Reload();

    private void OnPinCurrentTabClicked(object sender, RoutedEventArgs e)
    {
        if (_selected != null) PinTabRequested?.Invoke(this, _selected);
    }

    private static BrowserTab? TabFrom(object sender) => (sender as FrameworkElement)?.DataContext as BrowserTab;

    private void OnPinTabMenu(object sender, RoutedEventArgs e)
    {
        if (TabFrom(sender) is { } tab) PinTabRequested?.Invoke(this, tab);
    }

    private void OnUnpinTabMenu(object sender, RoutedEventArgs e) => UnpinRequested?.Invoke(this, EventArgs.Empty);
    private void OnReloadTabMenu(object sender, RoutedEventArgs e) => TabFrom(sender)?.WebView.CoreWebView2?.Reload();

    // ------------------------------------------------------------------ automatic reload

    public void SetReloadInterval(BrowserTab tab, int minutes)
    {
        tab.ReloadTimer?.Stop();
        tab.ReloadTimer = null;
        tab.ReloadMinutes = Math.Max(0, minutes);
        if (tab.ReloadMinutes > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(tab.ReloadMinutes) };
            timer.Tick += (_, _) =>
            {
                if (_closing || !_tabs.Contains(tab)) { timer.Stop(); return; }
                tab.WebView.CoreWebView2?.Reload();
            };
            timer.Start();
            tab.ReloadTimer = timer;
        }
        if (tab == PinnedTab) UpdateHeaderTab();
    }

    // ------------------------------------------------------------------ automatic recovery

    /// <summary>Wait until the next attempt, growing with each consecutive failure (does not hammer the site).</summary>
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)
    };

    /// <summary>The user turned automatic recovery on/off from the tab menu (MainWindow saves).</summary>
    public event EventHandler? AutoRecoverToggleRequested;

    private void OnNavigationResult(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            // The page loaded, but the server answered with a "down" error (500, 502, 503, 504, Cloudflare's 52x…).
            var status = e.HttpStatusCode;
            if (status >= 500 && status <= 599)
            {
                HandleFailure(tab, Loc.T("PV.SiteDownCode", status));
                return;
            }
            MarkHealthy(tab);
            return;
        }

        switch (e.WebErrorStatus)
        {
            case CoreWebView2WebErrorStatus.OperationCanceled:            // the user/page navigated elsewhere
            case CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired:
            case CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired:
            case CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect:  // certificate error: reloading does not fix it
            case CoreWebView2WebErrorStatus.CertificateExpired:
            case CoreWebView2WebErrorStatus.ClientCertificateContainsErrors:
            case CoreWebView2WebErrorStatus.CertificateRevoked:
            case CoreWebView2WebErrorStatus.CertificateIsInvalid:
                return;
            case CoreWebView2WebErrorStatus.Disconnected:
            case CoreWebView2WebErrorStatus.ConnectionAborted:
            case CoreWebView2WebErrorStatus.ConnectionReset:
                HandleFailure(tab, Loc.T("PV.ConnectionDropped"));
                return;
            case CoreWebView2WebErrorStatus.CannotConnect:
            case CoreWebView2WebErrorStatus.HostNameNotResolved:
            case CoreWebView2WebErrorStatus.ServerUnreachable:
            case CoreWebView2WebErrorStatus.Timeout:
                HandleFailure(tab, Loc.T("PV.SiteNoResponse"));
                return;
            default:
                HandleFailure(tab, Loc.T("PV.LoadFailed"));
                return;
        }
    }

    private void HandleFailure(BrowserTab tab, string reason, TimeSpan? firstDelay = null)
    {
        if (!_store.AutoRecover || _closing) return;

        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        {
            // No network at all: no point trying; reload when the connection returns (OnNetworkRestored).
            tab.WaitingForNetwork = true;
            SetRecoveryStatus(tab, Loc.T("PV.NoInternet"));
            return;
        }

        var delay = tab.FailureCount == 0 && firstDelay != null
            ? firstDelay.Value
            : Backoff[Math.Min(tab.FailureCount, Backoff.Length - 1)];
        tab.FailureCount++;
        SetRecoveryStatus(tab, Loc.T("PV.RetryIn", reason, FormatDelay(delay), tab.FailureCount));
        ScheduleRetry(tab, delay);
    }

    private void MarkHealthy(BrowserTab tab)
    {
        tab.FailureCount = 0;
        tab.WaitingForNetwork = false;
        SetRecoveryStatus(tab, null);
    }

    private void SetRecoveryStatus(BrowserTab tab, string? status)
    {
        tab.RecoveryStatus = status;
        if (tab == PinnedTab) UpdateHeaderTab();
    }

    private static string FormatDelay(TimeSpan d) =>
        d.TotalSeconds < 60 ? $"{d.TotalSeconds:0} s" : $"{d.TotalMinutes:0} min";

    /// <summary>The internet is back: reloads right away the tabs that were waiting or retrying.</summary>
    public void OnNetworkRestored()
    {
        if (!_store.AutoRecover || _closing) return;
        foreach (var tab in _tabs.Where(t => t.WaitingForNetwork || t.FailureCount > 0).ToList())
        {
            tab.WaitingForNetwork = false;
            tab.FailureCount = 0;
            SetRecoveryStatus(tab, Loc.T("PV.ConnectionBack"));
            try { tab.WebView.CoreWebView2?.Reload(); } catch (Exception) { }
        }
    }

    /// <summary>Automatic recovery was turned off: cancels the notices (already scheduled attempts do not run).</summary>
    public void OnAutoRecoverChanged()
    {
        if (_store.AutoRecover) return;
        foreach (var tab in _tabs) MarkHealthy(tab);
    }

    // ------------------------------------------------------------------ zoom

    private static readonly double[] ZoomSteps =
        { 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };

    /// <summary>The tab that receives the zoom: the grid cell's one, or the selected one in normal mode.</summary>
    private BrowserTab? ZoomTarget => _inGrid ? ShownTab : _selected;

    public void ZoomBy(int direction)
    {
        var tab = ZoomTarget;
        if (tab == null) return;
        var z = tab.WebView.ZoomFactor;
        tab.WebView.ZoomFactor = direction > 0
            ? ZoomSteps.FirstOrDefault(s => s > z + 0.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(s => s < z - 0.001, ZoomSteps[0]);
    }

    public void ResetZoom()
    {
        if (ZoomTarget is { } tab) tab.WebView.ZoomFactor = 1.0;
    }

    private void UpdateZoomUi()
    {
        var gridZoom = (_inGrid ? ShownTab : PinnedTab)?.WebView.ZoomFactor ?? 1.0;
        ZoomLabel.Content = $"{gridZoom * 100:0}%";

        var navZoom = _selected?.WebView.ZoomFactor ?? 1.0;
        NavZoomButton.Content = $"{navZoom * 100:0}%";
        NavZoomButton.Visibility = Math.Abs(navZoom - 1) > 0.001 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => ZoomBy(+1);
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => ZoomBy(-1);
    private void OnZoomResetClicked(object sender, RoutedEventArgs e) => ResetZoom();

    private void OnBackgroundModeMenu(object sender, RoutedEventArgs e)
    {
        if (TabFrom(sender) is { } tab && (sender as MenuItem)?.Tag is string m &&
            m.StartsWith("mode:") && Enum.TryParse<BackgroundMode>(m[5..], out var mode))
            SetBackgroundMode(tab, mode);
    }

    private void OnAutoRecoverMenu(object sender, RoutedEventArgs e) =>
        AutoRecoverToggleRequested?.Invoke(this, EventArgs.Empty);

    private void ScheduleRetry(BrowserTab tab, TimeSpan delay)
    {
        if (tab.RetryPending) return;
        tab.RetryPending = true;
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            tab.RetryPending = false;
            if (_closing || !_tabs.Contains(tab) || !_store.AutoRecover) return;
            try { tab.WebView.CoreWebView2?.Reload(); }
            catch (Exception) { /* the page may be in the middle of another failure; the next attempt sorts it out */ }
        };
        timer.Start();
    }

    private void OnTabMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.DataContext is not BrowserTab tab) return;
        UpdateMoveTabMenuItems(menu, tab);
        foreach (var item in menu.Items.OfType<MenuItem>())
            foreach (var sub in item.Items.OfType<MenuItem>())
                if (sub.Tag is string t && int.TryParse(t, out var minutes))
                    sub.IsChecked = minutes == tab.ReloadMinutes;
                else if (sub.Tag is "recover")
                    sub.IsChecked = _store.AutoRecover;
                else if (sub.Tag is string m && m.StartsWith("mode:") && Enum.TryParse<BackgroundMode>(m[5..], out var mode))
                    sub.IsChecked = mode == tab.Mode;
    }

    private void OnReloadIntervalMenu(object sender, RoutedEventArgs e)
    {
        if (TabFrom(sender) is { } tab && (sender as MenuItem)?.Tag is string t && int.TryParse(t, out var minutes))
            SetReloadInterval(tab, minutes);
    }


    // ------------------------------------------------------------------ resource usage

    /// <summary>IDs of this profile's browser processes (browser, pages, GPU…). Call on the UI thread.</summary>
    public IReadOnlyList<int> GetProcessIds()
    {
        try
        {
            return _env?.GetProcessInfos().Select(p => p.ProcessId).ToList() ?? new List<int>();
        }
        catch (Exception) { return new List<int>(); }
    }

    private void OnCloseTabMenu(object sender, RoutedEventArgs e)
    {
        if (TabFrom(sender) is { } tab) CloseTab(tab);
    }
    private void OnDevToolsClicked(object sender, RoutedEventArgs e) => _selected?.WebView.CoreWebView2?.OpenDevToolsWindow();

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _selected?.WebView.CoreWebView2 != null)
        {
            Navigate(_selected, AddressBar.Text);
            _selected.WebView.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _selected != null)
        {
            AddressBar.Text = DisplayUrl(_selected.Url);
            _selected.WebView.Focus();
            e.Handled = true;
        }
    }

    private void OnAddressFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBar.SelectAll();
        AddressPill.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
    }

    private void OnAddressBlur(object sender, KeyboardFocusChangedEventArgs e) =>
        AddressPill.BorderBrush = (System.Windows.Media.Brush)FindResource("Line");

    /// <summary>Padlock for https, globe for the rest.</summary>
    private void UpdateAddressIcon(string? url)
    {
        var secure = url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;
        AddressIcon.Text = secure ? "" : "";
        AddressIcon.ToolTip = secure ? Loc.T("PV.SecureConnection") : null;
    }

    private void OnAddressMouseDown(object sender, MouseButtonEventArgs e)
    {
        // The first click selects everything (like a browser); later clicks place the cursor.
        if (!AddressBar.IsKeyboardFocusWithin)
        {
            AddressBar.Focus();
            e.Handled = true;
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && CancelTabDrag()) { e.Handled = true; return; }

        if (ctrl && key is Key.OemPlus or Key.Add) { e.Handled = true; ZoomBy(+1); return; }
        if (ctrl && key is Key.OemMinus or Key.Subtract) { e.Handled = true; ZoomBy(-1); return; }
        if (ctrl && key is Key.D0 or Key.NumPad0) { e.Handled = true; ResetZoom(); return; }

        if (_inGrid)
        {
            var shown = ShownTab?.WebView.CoreWebView2;
            if (ctrl && key == Key.M) { e.Handled = true; ToggleMute(ShownTab); }
            else if (key == Key.F5 || (ctrl && key == Key.R)) { e.Handled = true; shown?.Reload(); }
            else if (key == Key.F12) { e.Handled = true; shown?.OpenDevToolsWindow(); }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Left) { e.Handled = true; shown?.GoBack(); }
            else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Right) { e.Handled = true; shown?.GoForward(); }
            return;
        }

        if (ctrl && key == Key.M) { e.Handled = true; ToggleMute(_selected); }
        else if (ctrl && key == Key.T) { e.Handled = true; await NewTabAndFocusAddressAsync(); }
        else if (ctrl && key == Key.W && _selected != null) { e.Handled = true; CloseTab(_selected); }
        else if (ctrl && key == Key.L) { e.Handled = true; AddressBar.Focus(); AddressBar.SelectAll(); }
        else if (ctrl && key == Key.Tab) { e.Handled = true; SelectRelative(shift ? -1 : 1); }
        else if (ctrl && shift && key == Key.PageUp) { e.Handled = true; MoveTab(_selected, -1); }
        else if (ctrl && shift && key == Key.PageDown) { e.Handled = true; MoveTab(_selected, +1); }
        else if (key == Key.F5 || (ctrl && key == Key.R)) { e.Handled = true; _selected?.WebView.CoreWebView2?.Reload(); }
        else if (key == Key.F12) { e.Handled = true; _selected?.WebView.CoreWebView2?.OpenDevToolsWindow(); }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Left) { e.Handled = true; _selected?.WebView.CoreWebView2?.GoBack(); }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Right) { e.Handled = true; _selected?.WebView.CoreWebView2?.GoForward(); }
    }
}
