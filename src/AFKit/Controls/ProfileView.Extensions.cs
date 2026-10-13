using AFKit.Services;
using Microsoft.Web.WebView2.Core;

namespace AFKit.Controls;

/// <summary>
/// Browser extensions of this profile's session. Operations go through a lock so the sync done
/// when the session opens and the extensions dialog do not install the same thing at the same time.
/// </summary>
public partial class ProfileView
{
    private readonly SemaphoreSlim _extensionLock = new(1, 1);

    /// <summary>
    /// The session's CoreWebView2Profile (needs a tab that already exists). Waits for the session to finish opening;
    /// null if it closes or does not open in time.
    /// </summary>
    public async Task<CoreWebView2Profile?> GetBrowserProfileAsync(int timeoutMs = 20000)
    {
        for (var waited = 0; ; waited += 200)
        {
            if (_closing) return null;
            var core = _tabs.Select(t => t.WebView.CoreWebView2).FirstOrDefault(c => c != null);
            if (core != null) return core.Profile;
            if (waited >= timeoutMs) return null;
            await Task.Delay(200);
        }
    }

    /// <summary>Runs an extensions operation with the browser profile, one at a time.</summary>
    public async Task<T> RunExtensionOperationAsync<T>(Func<CoreWebView2Profile, Task<T>> operation)
    {
        var browser = await GetBrowserProfileAsync()
                      ?? throw new ExtensionException(Loc.T("Ext.SessionNotOpen"));
        await _extensionLock.WaitAsync();
        try { return await operation(browser); }
        finally { _extensionLock.Release(); }
    }

    /// <summary>Applies the profile's extension record to the browser. True if anything changed.</summary>
    public Task<bool> SyncExtensionsAsync() =>
        RunExtensionOperationAsync(browser => ExtensionService.SyncAsync(browser, Profile));

    /// <summary>When the session opens, right after the first tab exists (before it navigates).</summary>
    private async Task ApplyExtensionsOnStartAsync(BrowserTab firstTab)
    {
        if (Profile.Extensions.Count == 0 && Profile.RemovedExtensionIds.Count == 0) return;
        if (firstTab.WebView.CoreWebView2 is not { } core) return;
        await _extensionLock.WaitAsync();
        try { await ExtensionService.SyncAsync(core.Profile, Profile); }
        catch (Exception) { /* extensions must not stop the session from opening; the dialog shows the problem */ }
        finally { _extensionLock.Release(); }
    }

    /// <summary>Reloads every tab (so new extensions' content scripts get into the pages).</summary>
    public void ReloadAllTabs()
    {
        foreach (var tab in _tabs.ToList())
        {
            try
            {
                var core = tab.WebView.CoreWebView2;
                if (core != null && !string.IsNullOrEmpty(core.Source) && core.Source != "about:blank") core.Reload();
            }
            catch (Exception) { /* tab closing */ }
        }
    }

    /// <summary>Opens any URL (e.g. chrome-extension://…) in a new tab, bypassing the address bar.</summary>
    public async Task OpenUrlInNewTabAsync(string url)
    {
        if (await GetBrowserProfileAsync() == null) return;
        // If the session just opened, wait for the extensions to be applied (the page may belong to one of them).
        await _extensionLock.WaitAsync();
        _extensionLock.Release();
        var tab = await CreateTabAsync(url: null, select: true);
        try { tab?.WebView.CoreWebView2?.Navigate(url); }
        catch (Exception) { /* URL refused by the browser */ }
    }
}
