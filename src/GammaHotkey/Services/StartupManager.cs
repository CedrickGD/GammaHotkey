using Microsoft.Win32;

namespace GammaHotkey.Services;

/// <summary>Manages the per-user "run at login" entry (HKCU Run – no admin needed).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GammaHotkey";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
                return;

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(AppName, $"\"{exe}\" --tray");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort; the checkbox just won't stick if the registry write fails.
        }
    }
}
