using GammaHotkey.Services;

namespace GammaHotkey.Models;

/// <summary>What kind of physical input fires a trigger.</summary>
public enum TriggerKind
{
    Keyboard,
    Mouse,
}

/// <summary>The mouse buttons we are willing to bind. Left/Right are intentionally
/// excluded from binding so normal clicking is never hijacked.</summary>
public enum MouseButton
{
    Middle,
    XButton1, // "Mouse 4" – the back side-button
    XButton2, // "Mouse 5" – the forward side-button
}

/// <summary>
/// A single trigger source: either a keyboard virtual-key (e.g. 0x7C = F13, which is
/// what a Logitech G HUB Lua script sends), or one of the bindable mouse buttons.
/// Value-equality lets it be used directly as a dictionary / hash-set key.
/// </summary>
public readonly record struct TriggerInput(TriggerKind Kind, int VirtualKey, MouseButton Button)
{
    public static TriggerInput Key(int vk) => new(TriggerKind.Keyboard, vk, default);

    public static TriggerInput Mouse(MouseButton button) => new(TriggerKind.Mouse, 0, button);

    public bool IsEmpty => Kind == TriggerKind.Keyboard && VirtualKey == 0;

    public static readonly TriggerInput None = new(TriggerKind.Keyboard, 0, default);

    /// <summary>Human-friendly label, e.g. "F13", "Mouse 4", "G".</summary>
    public string Describe()
    {
        if (IsEmpty)
            return string.Empty;

        return Kind switch
        {
            TriggerKind.Mouse => Button switch
            {
                MouseButton.Middle => "Mouse 3",
                MouseButton.XButton1 => "Mouse 4",
                MouseButton.XButton2 => "Mouse 5",
                _ => "Mouse",
            },
            _ => KeyNames.DisplayName(VirtualKey),
        };
    }

    public override string ToString() => Describe();
}
