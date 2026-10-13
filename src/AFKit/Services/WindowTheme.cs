using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AFKit.Services;

/// <summary>Dark title bar (Windows 10 20H1+) and a caption color matching the app panel (Windows 11).</summary>
public static class WindowTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the theme to every app window as soon as each one appears.</summary>
    public static void RegisterForAllWindows()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((Window)sender)));
    }

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));

            // Colors as COLORREF (0x00BBGGRR). Silently ignored before Windows 11.
            var caption = ToColorRef(0x1E, 0x20, 0x26);   // Panel  #1E2026
            var text = ToColorRef(0xE6, 0xE7, 0xEA);      // Fg     #E6E7EA
            var border = ToColorRef(0x30, 0x33, 0x3B);    // Line   #30333B
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static int ToColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
