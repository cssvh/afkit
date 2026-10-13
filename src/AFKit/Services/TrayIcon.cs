using Forms = System.Windows.Forms;

namespace AFKit.Services;

/// <summary>AFKit icon in the system tray (near the clock), with a menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _muteAllItem;
    private readonly Forms.ToolStripMenuItem _closeToTrayItem;
    private readonly Forms.ToolStripMenuItem _autoRecoverItem;
    private readonly Forms.ToolStripMenuItem _taskbarItem;

    public event EventHandler? OpenRequested;
    public event EventHandler? MuteAllToggled;
    public event EventHandler? CloseToTrayToggled;
    public event EventHandler? AutoRecoverToggled;
    public event EventHandler? ExitRequested;
    public event EventHandler? TaskbarToggled;
    public event EventHandler? TaskbarAllProfiles;
    public event EventHandler? TaskbarNoProfiles;
    /// <summary>The user picked a UI language (code: pt-BR, en or es).</summary>
    public event EventHandler<string>? LanguageSelected;

    public TrayIcon(System.Drawing.Icon icon)
    {
        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem(Loc.T("Tray.Open")) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _muteAllItem = new Forms.ToolStripMenuItem(Loc.T("Tray.MuteAll"));
        _muteAllItem.Click += (_, _) => MuteAllToggled?.Invoke(this, EventArgs.Empty);
        _autoRecoverItem = new Forms.ToolStripMenuItem(Loc.T("Tray.AutoRecover"));
        _autoRecoverItem.Click += (_, _) => AutoRecoverToggled?.Invoke(this, EventArgs.Empty);
        _closeToTrayItem = new Forms.ToolStripMenuItem(Loc.T("Tray.CloseToTray"));
        _closeToTrayItem.Click += (_, _) => CloseToTrayToggled?.Invoke(this, EventArgs.Empty);
        // V2 — battle window (task bar)
        _taskbarItem = new Forms.ToolStripMenuItem(Loc.T("Tray.Taskbar"));
        _taskbarItem.Click += (_, _) => TaskbarToggled?.Invoke(this, EventArgs.Empty);
        var taskbarAll = new Forms.ToolStripMenuItem(Loc.T("Tray.TaskbarAll"));
        taskbarAll.Click += (_, _) => TaskbarAllProfiles?.Invoke(this, EventArgs.Empty);
        var taskbarNone = new Forms.ToolStripMenuItem(Loc.T("Tray.TaskbarNone"));
        taskbarNone.Click += (_, _) => TaskbarNoProfiles?.Invoke(this, EventArgs.Empty);

        var language = new Forms.ToolStripMenuItem(Loc.T("Tray.Language"));
        foreach (var (code, name) in Loc.Languages)
        {
            var item = new Forms.ToolStripMenuItem(name) { Checked = code == Loc.Language };
            item.Click += (_, _) => { if (code != Loc.Language) LanguageSelected?.Invoke(this, code); };
            language.DropDownItems.Add(item);
        }

        var exit = new Forms.ToolStripMenuItem(Loc.T("Tray.Exit"));
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            open, new Forms.ToolStripSeparator(), _muteAllItem, _autoRecoverItem, _closeToTrayItem,
            new Forms.ToolStripSeparator(), _taskbarItem, taskbarAll, taskbarNone,
            new Forms.ToolStripSeparator(), language,
            new Forms.ToolStripSeparator(), exit
        });

        _icon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "AFKit",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetState(bool muteAll, bool closeToTray, bool autoRecover)
    {
        _muteAllItem.Checked = muteAll;
        _autoRecoverItem.Checked = autoRecover;
        _closeToTrayItem.Checked = closeToTray;
    }

    public void SetTaskbarState(bool enabled) => _taskbarItem.Checked = enabled;

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text; // Windows limit

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
