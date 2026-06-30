using System.Windows.Input;

namespace GammaHotkey.Services;

/// <summary>One selectable "G HUB key" – an F13..F24 key that a Logitech Lua
/// script can send and a normal keyboard can't physically produce.</summary>
public sealed record GhubKeyOption(string Display, int VirtualKey);

/// <summary>
/// Virtual-key helpers: friendly display names, the lowercase key-name strings the
/// G HUB / LGS Lua API expects (for <c>PressKey</c>), and the F13–F24 picker list.
/// </summary>
public static class KeyNames
{
    // Virtual-key codes we care about by name.
    public const int VK_ESCAPE = 0x1B;
    public const int VK_F13 = 0x7C;
    public const int VK_F24 = 0x87;

    private static readonly int[] ModifierVks =
    {
        0x10, 0x11, 0x12,             // SHIFT, CONTROL, MENU (generic)
        0xA0, 0xA1,                   // LSHIFT, RSHIFT
        0xA2, 0xA3,                   // LCONTROL, RCONTROL
        0xA4, 0xA5,                   // LMENU, RMENU
        0x5B, 0x5C,                   // LWIN, RWIN
        0x14, 0x90, 0x91,             // CAPSLOCK, NUMLOCK, SCROLLLOCK
    };

    /// <summary>True for keys we won't capture on their own (modifiers/locks).</summary>
    public static bool IsModifierOrLock(int vk) => Array.IndexOf(ModifierVks, vk) >= 0;

    /// <summary>The F13–F24 keys offered in the trigger picker for G HUB use.</summary>
    public static IReadOnlyList<GhubKeyOption> GHubKeyOptions { get; } = BuildGhubKeyOptions();

    private static GhubKeyOption[] BuildGhubKeyOptions()
    {
        var list = new List<GhubKeyOption>();
        for (int vk = VK_F13; vk <= VK_F24; vk++)
            list.Add(new GhubKeyOption($"F{13 + (vk - VK_F13)}", vk));
        return list.ToArray();
    }

    /// <summary>Friendly name for the UI, e.g. 124 -> "F13", 0x47 -> "G".</summary>
    public static string DisplayName(int vk)
    {
        if (vk == 0)
            return string.Empty;

        // F13..F24 are our headline keys – name them explicitly.
        if (vk is >= VK_F13 and <= VK_F24)
            return $"F{13 + (vk - VK_F13)}";

        Key key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            Key.None => $"VK 0x{vk:X2}",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Oem3 => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Oem1 => ";",
            Key.Oem7 => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString(),
        };
    }

    /// <summary>
    /// The lowercase key-name string the G HUB Lua API accepts for <c>PressKey</c>,
    /// or <c>null</c> if the key can't be expressed (so the Lua exporter can skip it).
    /// </summary>
    public static string? GhubKeyName(int vk)
    {
        // F1..F24
        if (vk is >= 0x70 and <= 0x87)
            return $"f{1 + (vk - 0x70)}";

        // A..Z
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)('a' + (vk - 0x41))).ToString();

        // Top-row 0..9
        if (vk is >= 0x30 and <= 0x39)
            return ((char)('0' + (vk - 0x30))).ToString();

        // Numpad 0..9
        if (vk is >= 0x60 and <= 0x69)
            return $"num{vk - 0x60}";

        return vk switch
        {
            0x1B => "escape",
            0x20 => "spacebar",
            0x0D => "enter",
            0x09 => "tab",
            0x08 => "backspace",
            0x2D => "insert",
            0x2E => "delete",
            0x24 => "home",
            0x23 => "end",
            0x21 => "pageup",
            0x22 => "pagedown",
            0x26 => "up",
            0x28 => "down",
            0x25 => "left",
            0x27 => "right",
            _ => null,
        };
    }
}
