using System.Windows;
using System.Windows.Interop;

namespace GammaHotkey.Services;

/// <summary>Turns the standard window title bar dark (Windows 10 1809+ / 11).</summary>
public static class DarkTitleBar
{
    public static void Apply(Window window)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            int on = 1;
            // Try the modern attribute first, then the pre-20H1 fallback.
            if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref on, sizeof(int));
        }
        catch
        {
            // Older Windows without dark title-bar support — harmless.
        }
    }
}
