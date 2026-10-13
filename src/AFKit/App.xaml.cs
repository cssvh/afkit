using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AFKit.Services;
using Microsoft.Web.WebView2.Core;

namespace AFKit;

public partial class App : Application
{
    // Single instance: opening AFKit again (from the shortcut) brings the existing window forward, even from the tray.
    private static readonly string InstanceId = $"AFKit.SingleInstance.{Environment.UserName}";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Restarted by the language switch: wait for the previous process to exit so the single-instance mutex is free.
        WaitForPreviousInstance(e.Args);

        // The saved language has to be known before any window (and its XAML texts) is created.
        ProfileStore.MigrateLegacyData();
        Loc.Initialize(Loc.PeekSavedLanguage(ProfileStore.DefaultDataFile));

        _instanceMutex = new Mutex(true, InstanceId, out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceId + ".Show");
        if (!isFirst)
        {
            _showSignal.Set(); // asks the instance that is already open to show itself
            Shutdown();
            return;
        }

        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var open = MessageBox.Show(
                Loc.T("App.NoWebView2"),
                "AFKit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (open == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            Shutdown();
            return;
        }

        WindowTheme.RegisterForAllWindows();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // Listens for requests from other instances to show the window.
        // The updater (install.ps1 -Auto) also signals ".Exit" to close AFKit saving the sessions, without asking.
        var signals = new WaitHandle[] { _showSignal, new EventWaitHandle(false, EventResetMode.AutoReset, InstanceId + ".Exit") };
        var listener = new Thread(() =>
        {
            while (true)
            {
                int which;
                try { which = WaitHandle.WaitAny(signals); }
                catch (ObjectDisposedException) { return; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (which == 1) (MainWindow as AFKit.MainWindow)?.RequestExit();
                    else (MainWindow as AFKit.MainWindow)?.RestoreFromTray();
                }));
            }
        }) { IsBackground = true, Name = "AFKit single-instance listener" };
        listener.Start();
    }

    /// <summary>Handles "--wait-pid N": waits (up to 30 s) for process N to exit.</summary>
    private static void WaitForPreviousInstance(string[] args)
    {
        var i = Array.IndexOf(args, "--wait-pid");
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(30_000);
        }
        catch (ArgumentException) { /* it already exited */ }
        catch (InvalidOperationException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, Loc.T("App.UnexpectedError"), MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
