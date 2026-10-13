using System.Reflection;
using System.Text.Json;
using AFKit.Models;
using Microsoft.Web.WebView2.Core;

namespace AFKit.Controls;

/// <summary>
/// Twitch and Kick lives reopen as they were: injects Assets/live-state.js, which reports the player's sound
/// (muted/volume) and quality; AFKit keeps that in the tab (it goes to afkit.json when the session closes) and hands it back
/// on reopen. The TAB's mute stays AFKit's own (IsMuted), independent of the player's sound.
/// </summary>
public partial class ProfileView
{
    private static string? _liveScript;

    private static string LiveScript
    {
        get
        {
            if (_liveScript != null) return _liveScript;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AFKit.LiveState.js");
            if (stream == null) return _liveScript = "";
            using var reader = new StreamReader(stream);
            return _liveScript = reader.ReadToEnd();
        }
    }

    private static bool IsLiveUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("kick.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".kick.com", StringComparison.OrdinalIgnoreCase));

    /// <param name="restore">Saved options of this tab (session reopening), or null.</param>
    private async Task AttachLiveStateAsync(BrowserTab tab, TabSetting? restore)
    {
        var core = tab.WebView.CoreWebView2;
        if (core == null) return;
        if (restore != null && (restore.LiveMuted != null || restore.LiveQuality != null))
        {
            tab.LiveMuted = restore.LiveMuted;
            tab.LiveVolume = restore.LiveVolume;
            tab.LiveQuality = restore.LiveQuality;
        }
        core.WebMessageReceived += (_, e) => OnLiveMessage(tab, e);

        var script = LiveScript;
        if (script.Length == 0) return;
        try
        {
            // Saved state available before the page is born: Kick's quality has to be in place
            // before the player loads.
            if (LiveStateJson(tab) is { } json)
                await core.AddScriptToExecuteOnDocumentCreatedAsync($"window.__afkitLive = {json};");
            await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }
        catch (Exception) { /* without the script: the live just opens however the site wants */ }
    }

    private static string? LiveStateJson(BrowserTab tab)
    {
        if (tab.LiveMuted == null && tab.LiveQuality == null) return null;
        return JsonSerializer.Serialize(new { muted = tab.LiveMuted, volume = tab.LiveVolume, quality = tab.LiveQuality });
    }

    private void OnLiveMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsLiveUrl(e.Source)) return;
        string json;
        try { json = e.WebMessageAsJson; }
        catch (Exception) { return; }
        if (json.Length > 4_000) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "afkit") != "live") return;
            switch (Str(root, "type"))
            {
                case "hello":
                    // New page (reopening, F5, another channel): hand back this tab's last known state.
                    if (LiveStateJson(tab) is { } state && tab.WebView.CoreWebView2 is { } core)
                    {
                        try { core.PostWebMessageAsJson("{\"afkitLive\":" + state + "}"); } catch (Exception) { }
                    }
                    break;

                case "state":
                    if (root.TryGetProperty("muted", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                        tab.LiveMuted = m.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var vol))
                        tab.LiveVolume = Math.Clamp(vol, 0, 1);
                    var q = Str(root, "quality");
                    if (!string.IsNullOrEmpty(q) && q.Length <= 200) tab.LiveQuality = q;
                    break;
            }
        }
        catch (JsonException) { }
    }
}
