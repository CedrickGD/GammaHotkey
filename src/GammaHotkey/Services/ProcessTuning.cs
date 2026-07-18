using System.Runtime.InteropServices;

namespace GammaHotkey.Services;

/// <summary>
/// Opts the process out of Windows 11 power throttling (EcoQoS).
///
/// Without this, when the app has no foreground window — e.g. it's minimized to the
/// system tray — Windows throttles its background threads to efficiency cores at
/// reduced speed. That starves the low-level keyboard/mouse hook: its callback then
/// blows past <c>LowLevelHooksTimeout</c> (~300 ms) and Windows stops delivering key
/// events to it, so the global hotkey "dies" in the tray and only comes back when the
/// window is brought to the foreground (which un-throttles the process). Disabling
/// throttling keeps the hook thread responsive regardless of window state.
/// </summary>
public static class ProcessTuning
{
    public static void DisablePowerThrottling()
    {
        try
        {
            var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
            {
                Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                // "I'm managing EXECUTION_SPEED" + "leave it OFF" == never throttle to eco speed.
                ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = 0,
            };

            NativeMethods.SetProcessInformation(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            // Not supported on older Windows — harmless.
        }
    }
}
