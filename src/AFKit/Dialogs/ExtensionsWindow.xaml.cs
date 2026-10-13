using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AFKit.Controls;
using AFKit.Models;
using AFKit.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace AFKit.Dialogs;

/// <summary>A row of the extensions list.</summary>
public sealed class ExtensionRow
{
    public ExtensionRow(ExtensionRecord record) => Record = record;

    public ExtensionRecord Record { get; }
    public string Name => string.IsNullOrWhiteSpace(Record.Name) ? Record.StoreId ?? Record.Id : Record.Name;
    public bool Enabled => Record.Enabled;
    public string Detail { get; init; } = "";
    public bool IsError { get; init; }
    public bool CanOpenOptions { get; init; }
    public string OptionsTooltip => Loc.T(CanOpenOptions ? "Ext.OptionsTip" : "Ext.NoOptionsTip");
    public string Tooltip => $"ID: {(string.IsNullOrEmpty(Record.Id) ? Record.StoreId : Record.Id)}" +
                             (string.IsNullOrEmpty(Record.SourceFolder) ? "" : $"\n{Loc.T("Ext.FolderLabel")}: {Record.SourceFolder}");
}

/// <summary>
/// A profile's extensions: install (Chrome Web Store link/ID, .crx/.zip or folder), enable/disable, options and remove.
/// Works with the session open (applies right away) or closed (applies when the session opens).
/// </summary>
public partial class ExtensionsWindow : Window
{
    private readonly Profile _profile;
    private readonly ExtensionService _service;
    private readonly IEnumerable<Profile> _allProfiles;
    private readonly Func<ProfileView?> _liveView;
    private readonly Action _save;
    private bool _busy;

    /// <summary>Options page the user asked to open (MainWindow opens it after the dialog closes).</summary>
    public string? UrlToOpen { get; private set; }

    public ExtensionsWindow(Profile profile, ExtensionService service, IEnumerable<Profile> allProfiles,
        Func<ProfileView?> liveView, Action save)
    {
        InitializeComponent();
        _profile = profile;
        _service = service;
        _allProfiles = allProfiles;
        _liveView = liveView;
        _save = save;
        Title = Loc.T("Ext.WindowTitle", profile.Name);
        HeaderText.Text = Loc.T("Ext.HeaderOf", profile.Name);
        Loaded += async (_, _) =>
        {
            StoreBox.Focus();
            await RefreshAsync(sync: true);
        };
    }

    // ================================================================ list

    private async Task RefreshAsync(bool sync)
    {
        var view = _liveView();
        IReadOnlyList<CoreWebView2BrowserExtension>? installed = null;
        if (view != null)
        {
            try
            {
                if (sync && await view.SyncExtensionsAsync())
                {
                    view.ReloadAllTabs();
                    _save();
                }
                installed = await view.RunExtensionOperationAsync(browser => browser.GetBrowserExtensionsAsync());
            }
            catch (Exception ex)
            {
                ShowStatus(Loc.T("Ext.ReadFailed", ex.Message), StatusKind.Error);
            }
        }
        Render(installed, live: view != null);
    }

    private void Render(IReadOnlyList<CoreWebView2BrowserExtension>? installed, bool live)
    {
        SessionHint.Text = Loc.T(live ? "Ext.SessionOpen" : "Ext.SessionClosed");
        ReloadTabsButton.IsEnabled = live && !_busy;

        var rows = new List<ExtensionRow>();
        foreach (var record in _profile.Extensions)
        {
            // Fills in missing information from the manifest (old or adopted records).
            if (!string.IsNullOrEmpty(record.SourceFolder) &&
                (record.OptionsPage == null || string.IsNullOrEmpty(record.Version) || string.IsNullOrEmpty(record.Name)))
            {
                var (name, version, options) = ExtensionService.ReadInfo(record.SourceFolder);
                if (string.IsNullOrEmpty(record.Name) && name != null) record.Name = name;
                if (string.IsNullOrEmpty(record.Version) && version != null) record.Version = version;
                record.OptionsPage ??= options;
            }

            string status;
            var isError = false;
            if (record.LastError != null)
            {
                status = record.LastError;
                isError = true;
            }
            else if (live && installed != null && ExtensionService.FindInstalled(installed, record) == null)
            {
                status = Loc.T("Ext.NotLoaded");
                isError = true;
            }
            else if (!live && string.IsNullOrEmpty(record.Id))
            {
                status = Loc.T("Ext.WillInstall");
            }
            else
            {
                status = Loc.T(record.Enabled ? "Ext.StatusActive" : "Ext.StatusDisabled");
            }

            var detail = string.IsNullOrEmpty(record.Version) ? status : $"{Loc.T("Ext.VersionLabel", record.Version)} · {status}";
            rows.Add(new ExtensionRow(record)
            {
                Detail = detail,
                IsError = isError,
                CanOpenOptions = !string.IsNullOrEmpty(record.OptionsPage) && !string.IsNullOrEmpty(record.Id)
            });
        }
        ExtensionList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================ install

    private async void OnInstallStoreClicked(object sender, RoutedEventArgs e) => await InstallFromStoreAsync();

    private async void OnStoreKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await InstallFromStoreAsync();
    }

    private async Task InstallFromStoreAsync()
    {
        var id = ExtensionService.ParseStoreId(StoreBox.Text);
        if (id == null)
        {
            ShowStatus(Loc.T("Ext.PasteLink"), StatusKind.Error);
            return;
        }
        await InstallAsync(() => _service.DownloadFromStoreAsync(id), Loc.T("Ext.Downloading"));
    }

    private async void OnInstallFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Ext.InstallFileTitle"),
            Filter = $"{Loc.T("Ext.ChromeExtension")} (*.crx;*.zip)|*.crx;*.zip|{Loc.T("Ext.AllFiles")} (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var path = dialog.FileName;
        await InstallAsync(() => _service.FromFileAsync(path), Loc.T("Ext.Unpacking"));
    }

    private async void OnInstallFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Ext.FolderPickTitle") };
        if (dialog.ShowDialog(this) != true) return;
        var folder = dialog.FolderName;
        await InstallAsync(() => _service.FromFolderAsync(folder), Loc.T("Ext.Copying"));
    }

    private async Task InstallAsync(Func<Task<ExtensionPackage>> obtain, string progressText)
    {
        if (_busy) return;
        SetBusy(true);
        ShowStatus(progressText, StatusKind.Info);
        try
        {
            var package = await obtain();
            var existing = _profile.Extensions.FirstOrDefault(r =>
                r.StoreId == package.ExtensionId ||
                string.Equals(r.SourceFolder, package.Folder, StringComparison.OrdinalIgnoreCase));
            var record = new ExtensionRecord
            {
                StoreId = package.ExtensionId,
                Name = package.Name,
                Version = package.Version,
                SourceFolder = package.Folder,
                OptionsPage = package.OptionsPage,
                Enabled = true
            };

            var view = _liveView();
            if (view != null)
            {
                ShowStatus(Loc.T("Ext.Installing", package.Name), StatusKind.Info);
                await view.RunExtensionOperationAsync(async browser =>
                {
                    // The previous version (same ID) leaves before the new one comes in.
                    var installed = await browser.GetBrowserExtensionsAsync();
                    foreach (var old in installed.Where(x => x.Id == package.ExtensionId ||
                                                             (existing != null && !string.IsNullOrEmpty(existing.Id) && x.Id == existing.Id)).ToList())
                    {
                        try { await old.RemoveAsync(); }
                        catch (Exception) { /* if it does not leave, the Add below says what happened */ }
                    }

                    CoreWebView2BrowserExtension added;
                    try { added = await browser.AddBrowserExtensionAsync(package.Folder); }
                    catch (Exception ex)
                    {
                        throw new ExtensionException(Loc.T("Ext.BrowserRefused", ex.Message), ex);
                    }
                    record.Id = added.Id;
                    if (!string.IsNullOrEmpty(added.Name) && !added.Name.StartsWith("__MSG_", StringComparison.Ordinal))
                        record.Name = added.Name;
                    return true;
                });
            }
            else if (existing != null && !string.IsNullOrEmpty(existing.Id))
            {
                // Session closed: the old version leaves and the new one comes in when the session opens.
                _profile.RemovedExtensionIds.Add(existing.Id);
            }

            var oldFolder = existing?.SourceFolder;
            if (existing != null) _profile.Extensions[_profile.Extensions.IndexOf(existing)] = record;
            else _profile.Extensions.Add(record);
            if (!string.IsNullOrEmpty(oldFolder) && !string.Equals(oldFolder, package.Folder, StringComparison.OrdinalIgnoreCase))
                _service.DeleteFolderIfUnused(oldFolder, _allProfiles);
            _save();
            StoreBox.Text = "";

            if (view != null)
            {
                view.ReloadAllTabs(); // content scripts only get into pages loaded after the installation
                ShowStatus(Loc.T("Ext.InstalledLive", record.Name), StatusKind.Success);
            }
            else
            {
                ShowStatus(Loc.T("Ext.InstalledLater", record.Name), StatusKind.Success);
            }
        }
        catch (ExtensionException ex) { ShowStatus(ex.Message, StatusKind.Error); }
        catch (Exception ex) { ShowStatus(Loc.T("Ext.InstallFailed", ex.Message), StatusKind.Error); }
        finally { SetBusy(false); }

        await RefreshAsync(sync: false);
    }

    // ================================================================ per-extension actions

    private static ExtensionRecord? RecordFrom(object sender) =>
        ((sender as FrameworkElement)?.DataContext as ExtensionRow)?.Record;

    private async void OnToggleClicked(object sender, RoutedEventArgs e)
    {
        if (RecordFrom(sender) is not { } record || sender is not CheckBox box) return;
        var enabled = box.IsChecked == true;
        if (_busy) { box.IsChecked = record.Enabled; return; }
        record.Enabled = enabled;
        SetBusy(true);
        try
        {
            var view = _liveView();
            if (view != null)
            {
                await view.RunExtensionOperationAsync(async browser =>
                {
                    var ext = ExtensionService.FindInstalled(await browser.GetBrowserExtensionsAsync(), record);
                    if (ext != null && ext.IsEnabled != enabled) await ext.EnableAsync(enabled);
                    return true;
                });
                ShowStatus(Loc.T(enabled ? "Ext.EnabledLive" : "Ext.DisabledLive", record.Name), StatusKind.Success);
            }
            else
            {
                ShowStatus(Loc.T(enabled ? "Ext.EnabledLater" : "Ext.DisabledLater", record.Name), StatusKind.Success);
            }
            record.LastError = null;
            _save();
        }
        catch (Exception ex)
        {
            record.Enabled = !enabled;
            ShowStatus(Loc.T(enabled ? "Ext.EnableFailed" : "Ext.DisableFailed", ex.Message), StatusKind.Error);
        }
        finally { SetBusy(false); }
        await RefreshAsync(sync: false);
    }

    private async void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || RecordFrom(sender) is not { } record) return;
        var confirm = MessageBox.Show(this, Loc.T("Ext.RemoveConfirm", record.Name), Loc.T("Ext.RemoveTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            var view = _liveView();
            if (view != null)
            {
                await view.RunExtensionOperationAsync(async browser =>
                {
                    var ext = ExtensionService.FindInstalled(await browser.GetBrowserExtensionsAsync(), record);
                    if (ext != null) await ext.RemoveAsync();
                    return true;
                });
            }
            else if (!string.IsNullOrEmpty(record.Id))
            {
                _profile.RemovedExtensionIds.Add(record.Id); // leaves the browser when the session opens
            }

            _profile.Extensions.Remove(record);
            _service.DeleteFolderIfUnused(record.SourceFolder, _allProfiles);
            _save();
            ShowStatus(Loc.T(view != null ? "Ext.RemovedLive" : "Ext.Removed", record.Name), StatusKind.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.T("Ext.RemoveFailed", ex.Message), StatusKind.Error);
        }
        finally { SetBusy(false); }
        await RefreshAsync(sync: false);
    }

    private void OnOptionsClicked(object sender, RoutedEventArgs e)
    {
        if (RecordFrom(sender) is not { } record || string.IsNullOrEmpty(record.OptionsPage) || string.IsNullOrEmpty(record.Id)) return;
        UrlToOpen = $"chrome-extension://{record.Id}/{record.OptionsPage}";
        Close();
    }

    private void OnReloadTabsClicked(object sender, RoutedEventArgs e)
    {
        if (_liveView() is not { } view) return;
        view.ReloadAllTabs();
        ShowStatus(Loc.T("Ext.TabsReloaded"), StatusKind.Success);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // ================================================================ state

    private enum StatusKind { Info, Success, Error }

    private void ShowStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusText.Foreground = kind switch
        {
            StatusKind.Error => (Brush)FindResource("Danger"),
            StatusKind.Success => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x7A)),
            _ => (Brush)FindResource("FgDim")
        };
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallPanel.IsEnabled = !busy;
        ExtensionList.IsEnabled = !busy;
        ReloadTabsButton.IsEnabled = !busy && _liveView() != null;
        Cursor = busy ? Cursors.AppStarting : null;
    }
}
