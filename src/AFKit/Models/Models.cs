using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AFKit.Services;

namespace AFKit.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class Profile : Observable
{
    private string _name = Loc.T("Profile.New");
    private string? _groupId;
    private string _color = "#5B8DEF";
    private string _icon = "";
    private string _startUrl = "https://www.google.com";
    private bool _isLoaded;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(Glyph)); } }
    public string? GroupId { get => _groupId; set => Set(ref _groupId, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    /// <summary>Emoji or up to 2 characters. Empty = initials of the name.</summary>
    public string Icon { get => _icon; set { if (Set(ref _icon, value)) Raise(nameof(Glyph)); } }
    public string StartUrl { get => _startUrl; set => Set(ref _startUrl, value); }
    public List<string> SavedTabs { get; set; } = new();
    /// <summary>Index (in SavedTabs) of the tab pinned to the grid.</summary>
    public int? GridTabIndex { get; set; }
    /// <summary>Indexes (in SavedTabs) of the muted tabs.</summary>
    public List<int> MutedTabIndexes { get; set; } = new();
    /// <summary>Per-tab options such as auto reload (index in SavedTabs).</summary>
    public List<TabSetting> TabOptions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Position inside the group in the sidebar.</summary>
    public int Order { get; set; }
    /// <summary>Browser extensions of this profile (AFKit's record; WebView2 keeps the installation in the profile folder).</summary>
    public List<ExtensionRecord> Extensions { get; set; } = new();
    /// <summary>WebView2 IDs of extensions removed while the session was closed: they go away the next time the session opens.</summary>
    public List<string> RemovedExtensionIds { get; set; } = new();

    [JsonIgnore]
    public string Glyph
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Icon)) return Icon.Trim();
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        }
    }

    /// <summary>True while the profile has an open browser session.</summary>
    [JsonIgnore]
    public bool IsLoaded { get => _isLoaded; set => Set(ref _isLoaded, value); }

    private bool _isPinned;
    /// <summary>Pinned to the grid (the order is kept in AFKitData.PinnedIds).</summary>
    [JsonIgnore]
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }

    private bool _isMuted;
    /// <summary>Whole profile muted (all tabs, including new ones).</summary>
    public bool IsMuted { get => _isMuted; set => Set(ref _isMuted, value); }

    private bool _showInTaskbar;
    /// <summary>V2: this profile shows up in the battle window (task bar).</summary>
    public bool ShowInTaskbar { get => _showInTaskbar; set => Set(ref _showInTaskbar, value); }

    /// <summary>V2: position and size of this profile's battle window (null = default position).</summary>
    public double[]? TaskbarBounds { get; set; }

    /// <summary>V2: this profile's battle window stays on top of other windows (on by default).</summary>
    public bool TaskbarOverlay { get; set; } = true;

    private string _usageText = "";
    /// <summary>E.g. "CPU 3% · 412 MB" (only while the session is open).</summary>
    [JsonIgnore]
    public string UsageText { get => _usageText; set => Set(ref _usageText, value); }

    private string _memoryText = "";
    /// <summary>E.g. "412 MB" (grid cell header).</summary>
    [JsonIgnore]
    public string MemoryText { get => _memoryText; set => Set(ref _memoryText, value); }

    private bool _isHeavy;
    /// <summary>High resource usage (highlights the usage text in the sidebar).</summary>
    [JsonIgnore]
    public bool IsHeavy { get => _isHeavy; set => Set(ref _isHeavy, value); }

    private bool _isActive;
    /// <summary>Profile shown in the main area.</summary>
    [JsonIgnore]
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

/// <summary>A browser extension installed (or about to be installed) in a profile.</summary>
public sealed class ExtensionRecord
{
    /// <summary>ID WebView2 gave the extension when it was loaded. Empty = not installed yet (installs when the session opens).</summary>
    public string Id { get; set; } = "";
    /// <summary>Chrome Web Store ID (or computed for local packages); also the folder name under AFKit\Extensions.</summary>
    public string? StoreId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>Unpacked folder the extension is loaded from (specific to this machine).</summary>
    public string SourceFolder { get; set; } = "";
    /// <summary>Options page relative to the extension root (options_ui.page or options_page), if any.</summary>
    public string? OptionsPage { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Last error while syncing with the browser (this run only).</summary>
    [JsonIgnore]
    public string? LastError { get; set; }
}

/// <summary>What a tab does while it is out of sight.</summary>
public enum BackgroundMode
{
    /// <summary>Stops drawing (saves CPU/GPU and video), but scripts and timers keep running.</summary>
    Light = 0,
    /// <summary>Keeps drawing as if it were on screen (more CPU).</summary>
    Active = 1,
    /// <summary>Freezes the page until it is back on screen (almost zero CPU).</summary>
    Suspended = 2
}

/// <summary>Saved options of a tab.</summary>
public sealed class TabSetting
{
    public int Index { get; set; }
    /// <summary>Reload every N minutes (0 = off).</summary>
    public int ReloadMinutes { get; set; }
    public BackgroundMode Mode { get; set; }
    /// <summary>Page zoom (1 = 100%; 0 in old files also means 100%).</summary>
    public double Zoom { get; set; } = 1;
    /// <summary>Obsolete (auto recovery is global now); kept so old files can still be read.</summary>
    public bool ReloadOnFailure { get; set; }
    /// <summary>Live (Twitch/Kick): was the player muted? (null = not a live / never reported)</summary>
    public bool? LiveMuted { get; set; }
    /// <summary>Live: player volume (0..1).</summary>
    public double? LiveVolume { get; set; }
    /// <summary>Live: quality, in the site's own format (Kick "160", Twitch the "video-quality" value).</summary>
    public string? LiveQuality { get; set; }
}

public sealed class ProfileGroup : Observable
{
    private string _name = Loc.T("Group.Default");
    private string _color = "#8A8F98";
    private bool _isExpanded = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
}

public sealed class AFKitData
{
    public int Version { get; set; } = 1;
    /// <summary>UI language code (pt-BR, en or es). Null = follow the Windows language.</summary>
    public string? Language { get; set; }
    public List<ProfileGroup> Groups { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public string? LastProfileId { get; set; }
    /// <summary>Profiles pinned to the grid, in cell order (max. 4).</summary>
    public List<string> PinnedIds { get; set; } = new();
    public bool GridMode { get; set; }
    /// <summary>Mute everything (all profiles).</summary>
    public bool MuteAll { get; set; }
    /// <summary>Automatically reloads pages that hang, crash, lose the connection or whose site is down.</summary>
    public bool AutoRecover { get; set; } = true;
    /// <summary>The X button sends AFKit to the tray instead of closing it.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>Grid split: fraction of the left column's width and of the top row's height.</summary>
    public double GridColumnRatio { get; set; } = 0.5;
    public double GridRowRatio { get; set; } = 0.5;
    /// <summary>The "AFKit keeps running in the tray" hint has already been shown.</summary>
    public bool TrayHintShown { get; set; }
    /// <summary>Sidebar collapsed into the icon strip (☰ button / Ctrl+B).</summary>
    public bool SidebarCollapsed { get; set; }
    /// <summary>V2: battle window turned on.</summary>
    public bool TaskbarEnabled { get; set; }
    /// <summary>V2 (old, unused): Overlay of the single window. Now lives in each Profile.</summary>
    public bool TaskbarOverlay { get; set; } = true;
    /// <summary>V2 (old, unused): position of the single window. Now lives in each Profile.</summary>
    public double[]? TaskbarBounds { get; set; }
}
