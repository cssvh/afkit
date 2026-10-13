using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AFKit.Models;
using AFKit.Services;
using Microsoft.Web.WebView2.Core;

namespace AFKit.Controls;

/// <summary>
/// V2 — task bar: injects the reader (Assets/taskbar-probe.js) into each tab and receives from it your Pokémon in play,
/// the enemy and the sprite frames. The reader only acts on pokeidle.io and only reads the page.
/// </summary>
public partial class ProfileView
{
    private static string? _probeScript;

    /// <summary>New state or sprite arrived from some game tab in this profile.</summary>
    public event EventHandler? GameUpdated;
    /// <summary>Your Pokémon hit the enemy (in the task bar tab): key of the move effect ("fx:DARK").</summary>
    public event EventHandler<string>? TaskbarHit;

    // Already decoded frames, by key ("out:461:1" back view, "out:60149:3" front view, "fx:DARK"), with each frame's duration.
    // Shared by the profile's tabs.
    private readonly Dictionary<string, (IReadOnlyList<ImageSource> Frames, int Ms)> _spriteFrames = new();

    public (IReadOnlyList<ImageSource> Frames, int Ms)? GetSprite(string key) =>
        _spriteFrames.TryGetValue(key, out var s) ? s : null;

    /// <summary>Prepends the current language's texts to a page script (read by the script as <c>window.__afkitText</c>).</summary>
    private static string LocalizeScript(string script) => Loc.ScriptPrelude() + script;

    private static string ProbeScript
    {
        get
        {
            if (_probeScript != null) return _probeScript;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFKit.TaskbarProbe.js");
            if (stream == null) return _probeScript = "";
            using var reader = new StreamReader(stream);
            return _probeScript = reader.ReadToEnd();
        }
    }

    private async Task AttachTaskbarProbeAsync(BrowserTab tab)
    {
        var core = tab.WebView.CoreWebView2;
        if (core == null) return;
        core.WebMessageReceived += (_, e) => OnProbeMessage(tab, e);
        core.SourceChanged += (_, _) =>
        {
            // Left the game: the tab no longer counts for the task bar.
            if (tab.Game != null && !IsGameUrl(core.Source))
            {
                tab.Game = null;
                GameUpdated?.Invoke(this, EventArgs.Empty);
            }
        };
        var script = ProbeScript;
        if (script.Length == 0) return;
        try { await core.AddScriptToExecuteOnDocumentCreatedAsync(script); }
        catch (Exception) { /* no reader in this tab: the task bar just does not show this profile */ }
    }

    private static bool IsGameUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.Equals("pokeidle.io", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".pokeidle.io", StringComparison.OrdinalIgnoreCase));

    private void OnProbeMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only accepts messages coming from the game (other pages cannot feed the battle window).
        if (!IsGameUrl(e.Source)) return;
        string json;
        try { json = e.WebMessageAsJson; }
        catch (Exception) { return; }
        if (json.Length > 4_000_000) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "afkit") != "tb") return;

            switch (Str(root, "type"))
            {
                case "state":
                    tab.Game = new GameSnapshot { Me = ReadPokemon(root, "me"), Foe = ReadPokemon(root, "foe") };
                    GameUpdated?.Invoke(this, EventArgs.Empty);
                    break;

                case "sprite":
                    var key = Str(root, "key");
                    if (string.IsNullOrEmpty(key) || !root.TryGetProperty("frames", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                    var frames = new List<ImageSource>();
                    foreach (var f in arr.EnumerateArray().Take(16))
                        if (f.ValueKind == JsonValueKind.String && DecodeDataUrl(f.GetString()) is { } img) frames.Add(img);
                    if (frames.Count == 0) return;
                    var ms = (int)Math.Clamp(Num(root, "ms") ?? 220, 20, 1000);
                    _spriteFrames[key] = (frames, ms);
                    if (!key.StartsWith("fx:", StringComparison.Ordinal)) GameUpdated?.Invoke(this, EventArgs.Empty);
                    break;

                case "hit":
                    var fx = Str(root, "fx");
                    if (tab == TaskbarTab && !string.IsNullOrEmpty(fx)) TaskbarHit?.Invoke(this, fx);
                    break;
            }
        }
        catch (JsonException) { /* message that is not from the reader */ }
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static PokemonInfo? ReadPokemon(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Object) return null;
        var pokeName = Str(p, "name");
        if (string.IsNullOrWhiteSpace(pokeName)) return null;
        return new PokemonInfo
        {
            Name = pokeName.Length > 40 ? pokeName[..40] : pokeName,
            Level = Num(p, "lv") is { } lv ? (int)Math.Clamp(lv, 0, 100_000) : null,
            Hp = Num(p, "hp"),
            MaxHp = Num(p, "mhp"),
            Xp = Num(p, "xp"),
            XpMax = Num(p, "xpMax"),
            Shiny = p.TryGetProperty("shiny", out var sh) && sh.ValueKind == JsonValueKind.True,
            SpriteKey = Str(p, "key")
        };
    }

    private static BitmapImage? DecodeDataUrl(string? url)
    {
        const string prefix = "data:image/png;base64,";
        if (url == null || !url.StartsWith(prefix, StringComparison.Ordinal) || url.Length > 600_000) return null;
        try
        {
            var bytes = Convert.FromBase64String(url[prefix.Length..]);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Asks the game tabs to resend state and sprites (the battle window just opened).</summary>
    public void RequestTaskbarResync()
    {
        foreach (var tab in _tabs)
        {
            if (tab.WebView.CoreWebView2 is not { } core || !IsGameUrl(core.Source)) continue;
            try { core.PostWebMessageAsString("afkit-tb-resync"); } catch (Exception) { }
        }
    }

    /// <summary>
    /// The tab that goes to the task bar: the one pinned to the grid, if any; otherwise the first tab with the game.
    /// </summary>
    public BrowserTab? TaskbarTab => PinnedTab ?? _tabs.FirstOrDefault(t => t.Game != null);

    /// <summary>State and frames the battle window needs for this profile (null = nothing to show).</summary>
    public (GameSnapshot Game, IReadOnlyList<ImageSource> MeFrames, IReadOnlyList<ImageSource> FoeFrames)? GetTaskbarData()
    {
        var game = TaskbarTab?.Game;
        if (game?.Me == null) return null;
        IReadOnlyList<ImageSource> Frames(string? key) =>
            key != null && _spriteFrames.TryGetValue(key, out var f) ? f.Frames : Array.Empty<ImageSource>();
        return (game, Frames(game.Me.SpriteKey), Frames(game.Foe?.SpriteKey));
    }

    /// <summary>Session summary and captures of the task bar tab (for the strip below the battle).</summary>
    public (SessionSummary? Summary, CapturesInfo? Captures) GetTaskbarInfo()
    {
        var tab = TaskbarTab;
        return (tab?.Summary, tab?.Captures);
    }
}
