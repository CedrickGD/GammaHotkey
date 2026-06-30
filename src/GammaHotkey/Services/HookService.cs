using System.Runtime.InteropServices;
using System.Windows.Threading;
using GammaHotkey.Models;

namespace GammaHotkey.Services;

/// <summary>
/// Installs global low-level keyboard + mouse hooks on a dedicated message-pumped
/// thread. In normal mode it raises <see cref="TriggerFired"/> for bound triggers
/// while listening; in capture mode the next key / mouse button is reported once
/// (used by the key-capture control). All UI-facing callbacks are marshaled onto
/// the WPF dispatcher.
/// </summary>
public sealed class HookService : IDisposable
{
    private readonly Dispatcher _dispatcher;

    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);

    private IntPtr _kbHook;
    private IntPtr _mouseHook;
    private NativeMethods.HookProc? _kbProc;     // kept alive so the GC can't collect them
    private NativeMethods.HookProc? _mouseProc;

    private volatile bool _listening;
    private volatile bool _swallowMouse = true;
    private volatile HashSet<TriggerInput> _bound = new();

    private readonly object _captureLock = new();
    private bool _capturing;                 // guarded by _captureLock
    private Action<TriggerInput>? _onCaptured;
    private Action? _onCaptureCancelled;

    public HookService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Raised (on the UI thread) when a bound trigger fires while listening.</summary>
    public event Action<TriggerInput>? TriggerFired;

    public bool IsListening
    {
        get => _listening;
        set => _listening = value;
    }

    /// <summary>Refreshes the set of triggers that count as "bound" (for swallow + raise).</summary>
    public void UpdateBindings(IEnumerable<TriggerInput> bound, bool swallowMouse)
    {
        _bound = new HashSet<TriggerInput>(bound);
        _swallowMouse = swallowMouse;
    }

    public void Start()
    {
        if (_thread != null)
            return;
        _thread = new Thread(PumpThread)
        {
            IsBackground = true,
            Name = "GammaHotkey.HookPump",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(2000);
    }

    public void Stop()
    {
        if (_thread == null)
            return;
        if (_threadId != 0)
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _thread = null;
        _threadId = 0;
        _ready.Reset();
    }

    /// <summary>Arms one-shot capture of the next key / mouse button.</summary>
    public void BeginCapture(Action<TriggerInput> onCaptured, Action onCancelled)
    {
        lock (_captureLock)
        {
            _onCaptured = onCaptured;
            _onCaptureCancelled = onCancelled;
            _capturing = true;
        }
    }

    public void CancelCapture()
    {
        Action? cancelled = null;
        lock (_captureLock)
        {
            if (_capturing)
            {
                cancelled = _onCaptureCancelled;
                ClearCaptureLocked();
            }
        }
        if (cancelled != null)
            _dispatcher.BeginInvoke(cancelled);
    }

    // ------------------------------------------------------------ pump thread

    private void PumpThread()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _kbProc = KeyboardProc;
        _mouseProc = MouseProc;
        IntPtr hMod = NativeMethods.GetModuleHandle(null);
        _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
        _ready.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_kbHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _kbHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var k = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)k.vkCode;

                if (TryHandleCaptureKey(vk, out bool swallowCapture))
                {
                    if (swallowCapture)
                        return (IntPtr)1;
                }
                else if (_listening)
                {
                    var t = TriggerInput.Key(vk);
                    if (_bound.Contains(t))
                    {
                        RaiseTrigger(t);
                        return (IntPtr)1; // swallow so the hotkey doesn't also type
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            TriggerInput? t = null;

            if (msg == NativeMethods.WM_MBUTTONDOWN)
            {
                t = TriggerInput.Mouse(MouseButton.Middle);
            }
            else if (msg == NativeMethods.WM_XBUTTONDOWN)
            {
                var m = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                int xb = (short)(m.mouseData >> 16);
                t = xb switch
                {
                    NativeMethods.XBUTTON1 => TriggerInput.Mouse(MouseButton.XButton1),
                    NativeMethods.XBUTTON2 => TriggerInput.Mouse(MouseButton.XButton2),
                    _ => null,
                };
            }

            if (t is { } trigger)
            {
                if (TryHandleCaptureMouse(trigger, out bool swallowCapture))
                {
                    if (swallowCapture)
                        return (IntPtr)1;
                }
                else if (_listening && _bound.Contains(trigger))
                {
                    RaiseTrigger(trigger);
                    if (_swallowMouse)
                        return (IntPtr)1; // stop the side button also paging back, etc.
                }
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>
    /// Handles a keyboard event while capture is armed, atomically under the lock.
    /// Returns true if we are in capture mode (and sets <paramref name="swallow"/>);
    /// false if not capturing, so the caller runs its normal dispatch path.
    /// </summary>
    private bool TryHandleCaptureKey(int vk, out bool swallow)
    {
        swallow = false;
        Action<TriggerInput>? captured = null;
        Action? cancelled = null;
        lock (_captureLock)
        {
            if (!_capturing)
                return false;

            if (vk == KeyNames.VK_ESCAPE)
            {
                cancelled = _onCaptureCancelled;
                ClearCaptureLocked();
                swallow = true;
            }
            else if (!KeyNames.IsModifierOrLock(vk))
            {
                captured = _onCaptured;
                ClearCaptureLocked();
                swallow = true;
            }
            // else: a lone modifier / lock – stay armed and let it pass through.
        }

        if (captured != null)
        {
            var t = TriggerInput.Key(vk);
            _dispatcher.BeginInvoke(() => captured(t));
        }
        if (cancelled != null)
            _dispatcher.BeginInvoke(cancelled);
        return true;
    }

    private bool TryHandleCaptureMouse(TriggerInput trigger, out bool swallow)
    {
        swallow = false;
        Action<TriggerInput>? captured = null;
        lock (_captureLock)
        {
            if (!_capturing)
                return false;
            captured = _onCaptured;
            ClearCaptureLocked();
            swallow = true;
        }
        if (captured != null)
            _dispatcher.BeginInvoke(() => captured(trigger));
        return true;
    }

    private void ClearCaptureLocked()
    {
        _capturing = false;
        _onCaptured = null;
        _onCaptureCancelled = null;
    }

    private void RaiseTrigger(TriggerInput t)
    {
        var handler = TriggerFired;
        if (handler != null)
            _dispatcher.BeginInvoke(() => handler(t));
    }

    public void Dispose() => Stop();
}
