namespace GammaHotkey.Services;

/// <summary>
/// Process-wide handle to the running <see cref="HookService"/> so the key-capture
/// control can arm a one-shot capture without a constructor dependency. Set once by
/// the app at startup.
/// </summary>
public static class InputCapture
{
    public static HookService? Service { get; set; }
}
