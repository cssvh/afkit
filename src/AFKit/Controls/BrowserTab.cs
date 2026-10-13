using AFKit.Models;
using AFKit.Services;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace AFKit.Controls;

public sealed class BrowserTab : Observable
{
    private string _title = Loc.T("Tab.New");
    private string _url = "";
    private ImageSource? _favicon;
    private bool _isSelected;
    private bool _isLoading;

    public BrowserTab(WebView2 webView) => WebView = webView;

    public WebView2 WebView { get; }

    public string Title { get => _title; set => Set(ref _title, string.IsNullOrWhiteSpace(value) ? Loc.T("Tab.New") : value); }
    public string Url { get => _url; set => Set(ref _url, value); }
    public ImageSource? Favicon { get => _favicon; set => Set(ref _favicon, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _isMuted;
    /// <summary>The tab itself was muted by the user.</summary>
    public bool IsMuted { get => _isMuted; set { if (Set(ref _isMuted, value)) Raise(nameof(EffectiveMuted)); } }

    private bool _isProfileMuted;
    /// <summary>The whole profile is muted.</summary>
    public bool IsProfileMuted { get => _isProfileMuted; set { if (Set(ref _isProfileMuted, value)) Raise(nameof(EffectiveMuted)); } }

    /// <summary>Actually without sound (by the tab or by the profile).</summary>
    public bool EffectiveMuted => IsMuted || IsProfileMuted;

    // Live (Twitch/Kick): how the player was, reported by the page (saved when the session closes).
    public bool? LiveMuted { get; set; }
    public double? LiveVolume { get; set; }
    public string? LiveQuality { get; set; }

    private bool _isPlayingAudio;
    /// <summary>The page is playing audio right now (even if muted).</summary>
    public bool IsPlayingAudio { get => _isPlayingAudio; set => Set(ref _isPlayingAudio, value); }

    private int _reloadMinutes;
    /// <summary>Reload every N minutes (0 = off).</summary>
    public int ReloadMinutes
    {
        get => _reloadMinutes;
        set { if (Set(ref _reloadMinutes, value)) { Raise(nameof(HasAutoReload)); Raise(nameof(AutoReloadTooltip)); } }
    }

    public bool HasAutoReload => ReloadMinutes > 0;

    public string AutoReloadTooltip => ReloadMinutes > 0 ? Loc.T("Tab.ReloadsEvery", ReloadMinutes) : "";

    private string? _recoveryStatus;
    /// <summary>E.g. "No internet — reloads when the connection returns". Null = all good.</summary>
    public string? RecoveryStatus
    {
        get => _recoveryStatus;
        set { if (Set(ref _recoveryStatus, value)) Raise(nameof(IsRecovering)); }
    }

    public bool IsRecovering => RecoveryStatus != null;

    /// <summary>Consecutive failures (defines the wait until the next attempt).</summary>
    internal int FailureCount { get; set; }
    /// <summary>Failed without a network: reloads as soon as the internet is back.</summary>
    internal bool WaitingForNetwork { get; set; }

    /// <summary>Timer of the periodic reload (managed by ProfileView).</summary>
    internal System.Windows.Threading.DispatcherTimer? ReloadTimer { get; set; }
    /// <summary>Reload attempt scheduled after a failure (avoids several at once).</summary>
    internal bool RetryPending { get; set; }

    private BackgroundMode _mode;
    /// <summary>Behavior while the tab is out of sight.</summary>
    public BackgroundMode Mode
    {
        get => _mode;
        set { if (Set(ref _mode, value)) { Raise(nameof(IsActiveMode)); Raise(nameof(IsSuspendedMode)); } }
    }
    public bool IsActiveMode => Mode == BackgroundMode.Active;
    public bool IsSuspendedMode => Mode == BackgroundMode.Suspended;

    /// <summary>Page zoom (1 = 100%).</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>V2: last state read from the game in this tab (null = not PokeIdle).</summary>
    public GameSnapshot? Game { get; set; }

    /// <summary>V2: session summary the grid script sent from this tab (for the battle window's strip).</summary>
    public SessionSummary? Summary { get; set; }

    /// <summary>V2: record and latest captures the grid script sent from this tab.</summary>
    public CapturesInfo? Captures { get; set; }

    /// <summary>V2: the game's Economy Mode is on in this tab (null = unknown / not PokeIdle). Reported by the grid script.</summary>
    public bool? Economy { get; set; }

    private bool _isPinned;
    /// <summary>This is the profile's tab that shows up in the grid.</summary>
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }
}
