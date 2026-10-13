using System.Windows;
using AFKit.Dialogs;
using AFKit.Services;

namespace AFKit;

/// <summary>Per-profile browser extensions (profile menu → "Extensions…").</summary>
public partial class MainWindow
{
    private ExtensionService? _extensionService;

    private async void OnExtensionsMenu(object sender, RoutedEventArgs e)
    {
        if (ProfileFrom(sender) is not { } p) return;
        _extensionService ??= new ExtensionService(_store);

        var dialog = new ExtensionsWindow(p, _extensionService, _store.Profiles,
            () => _views.TryGetValue(p.Id, out var v) ? v : null, SaveStore) { Owner = this };
        dialog.ShowDialog();
        SaveStore();

        // An extension's "Options": opens the page in a new tab of the profile (opening the session if needed).
        if (dialog.UrlToOpen is { } url)
        {
            ShowSingle(p);
            if (_views.TryGetValue(p.Id, out var view)) await view.OpenUrlInNewTabAsync(url);
        }
    }
}
