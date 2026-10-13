using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using AFKit.Models;
using Microsoft.Web.WebView2.Core;

namespace AFKit.Controls;

/// <summary>
/// Grid mode ↔ PokeIdle: injects Assets/grid-overlay.js into each tab and talks to it over three channels:
/// "afkit-grid:1/0" (the tab is in a grid cell: turns Immersive Mode and the watermarks on/off),
/// "afkit-info:1/0" (the tab is the battle window's one: sends the session summary and the captures) and
/// "afkit-vis:1/0" (the tab is in view on screen). The page answers with "summary" and "caps" for the
/// battle window's info strip.
/// </summary>
public partial class ProfileView
{
    private static string? _gridScript;

    // Last value sent to each tab, per channel (true = ":1").
    private readonly Dictionary<BrowserTab, bool> _gridSent = new();
    private readonly Dictionary<BrowserTab, bool> _infoSent = new();
    private readonly Dictionary<BrowserTab, bool> _visSent = new();

    /// <summary>Already decoded capture portraits, by "k" (with the source data URL, to notice if it changed).</summary>
    private readonly Dictionary<string, (string Img, ImageSource? Image)> _captureImages = new();

    private const int MaxGridMessage = 10_000;
    /// <summary>"caps" carries up to 6 base64 PNG portraits.</summary>
    private const int MaxCapsMessage = 600_000;

    private static string GridScript
    {
        get
        {
            if (_gridScript != null) return _gridScript;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFKit.GridOverlay.js");
            if (stream == null) return _gridScript = "";
            using var reader = new StreamReader(stream);
            return _gridScript = LocalizeScript(reader.ReadToEnd());
        }
    }

    private async Task AttachGridOverlayAsync(BrowserTab tab)
    {
        var core = tab.WebView.CoreWebView2;
        if (core == null) return;
        core.WebMessageReceived += (_, e) => OnGridMessage(tab, e);
        core.SourceChanged += (_, _) =>
        {
            // Left the game: this tab's info no longer counts for the battle window.
            if ((tab.Summary != null || tab.Captures != null || tab.Economy != null) && !IsGameUrl(core.Source))
            {
                tab.Summary = null;
                tab.Captures = null;
                tab.Economy = null;
                UpdateHeaderTab();
                GameUpdated?.Invoke(this, EventArgs.Empty);
            }
        };
        var script = GridScript;
        if (script.Length == 0) return;
        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(script); }
        catch (Exception) { /* without the script: the cell just does not turn immersive mode on or show the watermarks */ }
    }

    private void OnGridMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsGameUrl(e.Source)) return;
        string json;
        try { json = e.WebMessageAsJson; }
        catch (Exception) { return; }
        if (json.Length > MaxCapsMessage) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "afkit") != "grid") return;
            var type = Str(root, "type");
            // Only "caps" can be big (portraits); the rest is small.
            if (type != "caps" && json.Length > MaxGridMessage) return;

            // Summary and captures only count from the tab that received "afkit-info:1" (a late one may arrive after the "0").
            if ((type is "summary" or "caps") && !(_infoSent.TryGetValue(tab, out var infoOn) && infoOn)) return;

            switch (type)
            {
                case "eco":
                    // The game's Economy Mode state (changes when the user toggles it in the page too).
                    var eco = root.TryGetProperty("on", out var ecoOn) && ecoOn.ValueKind == JsonValueKind.True;
                    if (tab.Economy == eco) return;
                    tab.Economy = eco;
                    UpdateHeaderTab();
                    break;

                case "ready":
                    // The game (re)loaded, e.g. F5: the new page knows nothing, so resend this tab's state.
                    UpdateGridOverlays(resend: tab);
                    break;

                case "summary":
                    var summary = new SessionSummary(
                        Clip(Str(root, "time")) ?? "", Clip(Str(root, "xp")) ?? "",
                        Clip(Str(root, "gold")) ?? "", Clip(Str(root, "profit")));
                    if (summary == tab.Summary) return; // nothing changed: leave the battle window alone
                    tab.Summary = summary;
                    GameUpdated?.Invoke(this, EventArgs.Empty);
                    break;

                case "caps":
                    var record = root.TryGetProperty("record", out var r) && r.ValueKind == JsonValueKind.Object ? ReadCaptureRow(r) : null;
                    var recent = new List<CaptureRow>();
                    if (root.TryGetProperty("recent", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var item in arr.EnumerateArray().Take(5))
                            if (item.ValueKind == JsonValueKind.Object && ReadCaptureRow(item) is { } row) recent.Add(row);
                    var caps = new CapturesInfo(record, recent);
                    if (tab.Captures?.Signature == caps.Signature) return;
                    tab.Captures = caps;
                    GameUpdated?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (JsonException) { }
    }

    /// <summary>Asks the page ("afkit-eco:1/0") to turn the game's Economy Mode on/off; only meaningful in a grid cell.</summary>
    private void SetEconomy(BrowserTab? tab, bool on)
    {
        if (tab?.WebView.CoreWebView2 is not { } core || !IsGameUrl(core.Source)) return;
        try { core.PostWebMessageAsString(on ? "afkit-eco:1" : "afkit-eco:0"); }
        catch (Exception) { /* tab closing */ }
    }

    private static string? Clip(string? text, int max = 40) =>
        text == null ? null : text.Length > max ? text[..max] : text;

    private CaptureRow? ReadCaptureRow(JsonElement o)
    {
        var name = Clip(Str(o, "name"));
        if (string.IsNullOrWhiteSpace(name)) return null;
        var k = Clip(Str(o, "k"), 120) ?? "";
        var level = Clip(Str(o, "level"), 16) ?? "";
        var time = Clip(Str(o, "time"), 16) ?? "";
        var img = Str(o, "img");
        var border = Clip(Str(o, "border"), 64);
        var shiny = o.TryGetProperty("shiny", out var sh) && sh.ValueKind == JsonValueKind.True;

        var seals = new List<CaptureSeal>();
        var sealsSig = new List<string>();
        if (o.TryGetProperty("seals", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray().Take(8))
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                var t = Clip(Str(s, "t"), 12);
                if (string.IsNullOrWhiteSpace(t)) continue;
                var bg = Clip(Str(s, "bg"), 64);
                var color = Clip(Str(s, "color"), 64);
                seals.Add(new CaptureSeal
                {
                    Text = t,
                    Background = CssColor.BrushOr(bg, CssColor.SealBackground),
                    Foreground = CssColor.BrushOr(color, CssColor.White)
                });
                sealsSig.Add(t + "|" + bg + "|" + color);
            }
        }

        return new CaptureRow
        {
            Name = name,
            Level = level,
            Time = time,
            Shiny = shiny,
            Portrait = CaptureImage(k, img),
            BorderBrush = CssColor.BrushOr(border, CssColor.DefaultBorder),
            Seals = seals,
            Signature = string.Join("\u001f", k, name, level, time, shiny ? "1" : "0", border ?? "",
                img == null ? "0" : $"{img.Length}:{img.GetHashCode()}", string.Join(",", sealsSig))
        };
    }

    /// <summary>Portrait of a capture: decodes once per "k" (again only if the image changes).</summary>
    private ImageSource? CaptureImage(string k, string? img)
    {
        if (img == null) return null;
        if (k.Length == 0) return DecodeDataUrl(img);
        if (_captureImages.TryGetValue(k, out var cached) && string.Equals(cached.Img, img, StringComparison.Ordinal))
            return cached.Image;
        if (_captureImages.Count >= 64) _captureImages.Clear(); // do not let the cache grow without limit
        var image = DecodeDataUrl(img);
        _captureImages[k] = (img, image);
        return image;
    }

    /// <summary>
    /// Re-evaluates the three channels of all tabs (called by MainWindow when the task bar changes). Cheap:
    /// only posts what changed.
    /// </summary>
    public void RefreshPageSignals() => UpdateGridOverlays();

    /// <summary>
    /// Tells each game tab whether it is in a grid cell, whether it is the battle window's tab and whether it is in view.
    /// Only sends when it changes; "afkit-grid:0" only goes to tabs that had received "afkit-grid:1" (so the immersive mode of
    /// a tab outside the grid is left alone). <paramref name="resend"/>: that tab's page just finished loading.
    /// </summary>
    private void UpdateGridOverlays(BrowserTab? resend = null)
    {
        foreach (var sent in new[] { _gridSent, _infoSent, _visSent })
            foreach (var gone in sent.Keys.Where(t => !_tabs.Contains(t)).ToList()) sent.Remove(gone);

        var gridTarget = _inGrid ? ShownTab : null;
        var infoTarget = _store.TaskbarEnabled && Profile.ShowInTaskbar ? TaskbarTab : null;
        var visTarget = _onScreen ? ShownTab : null;

        foreach (var tab in _tabs)
        {
            // The visible one first: the page only opens the game's panels while it is hidden.
            Signal(tab, _visSent, "afkit-vis", tab == visTarget, tab == resend, zeroOnlyAfterOne: false);
            Signal(tab, _gridSent, "afkit-grid", tab == gridTarget, tab == resend, zeroOnlyAfterOne: true);
            if (Signal(tab, _infoSent, "afkit-info", tab == infoTarget, tab == resend, zeroOnlyAfterOne: false) == false)
            {
                // This tab stopped sending: what it sent before goes stale; it disappears until it sends again.
                tab.Summary = null;
                tab.Captures = null;
            }
        }
    }

    /// <summary>
    /// Posts "<paramref name="channel"/>:1/0" if the value changed (or if the page reloaded).
    /// Returns the value sent, or null if nothing was sent.
    /// </summary>
    private bool? Signal(BrowserTab tab, Dictionary<BrowserTab, bool> sentByTab, string channel, bool want, bool resend,
        bool zeroOnlyAfterOne)
    {
        var known = sentByTab.TryGetValue(tab, out var sent);
        if (known && sent == want && !resend) return null;
        // Never received anything: "0" does not need to go (except info/vis when the page just loaded).
        if (!want && !known && (zeroOnlyAfterOne || !resend)) return null;
        if (tab.WebView.CoreWebView2 is not { } core || !IsGameUrl(core.Source)) return null;
        try
        {
            core.PostWebMessageAsString(channel + (want ? ":1" : ":0"));
            sentByTab[tab] = want;
            return want;
        }
        catch (Exception) { return null; /* tab closing: the next page's "ready" sorts it out */ }
    }
}
