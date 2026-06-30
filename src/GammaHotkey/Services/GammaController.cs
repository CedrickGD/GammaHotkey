using System.Runtime.InteropServices;
using GammaHotkey.Models;
using Microsoft.Win32;

namespace GammaHotkey.Services;

/// <summary>A display attached to the desktop.</summary>
public sealed record MonitorInfo(string DeviceName, string FriendlyName, bool IsPrimary);

/// <summary>
/// Applies a gamma value to the display(s) via the GDI gamma ramp – the same
/// hardware LUT the NVIDIA / AMD / Intel control-panel gamma sliders write to,
/// so it works on any GPU. Higher gamma = brighter mid-tones; 1.0 = neutral.
/// </summary>
public sealed class GammaController : IDisposable
{
    private readonly object _gate = new();

    // Original ramp per display device name; "" means "primary via GetDC(0)".
    private readonly Dictionary<string, ushort[]> _originals = new();

    private double _currentGamma = GammaPresets.Default;
    private bool _allMonitors = true;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);

    public GammaController()
    {
        CaptureOriginals();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>The gamma value most recently applied by the app.</summary>
    public double CurrentGamma
    {
        get { lock (_gate) return _currentGamma; }
    }

    /// <summary>Chooses which displays gamma is applied to (all, or a named subset).</summary>
    public void SetTargets(bool allMonitors, IEnumerable<string> selectedDeviceNames)
    {
        lock (_gate)
        {
            _allMonitors = allMonitors;
            _selected.Clear();
            foreach (var n in selectedDeviceNames)
                if (!string.IsNullOrEmpty(n))
                    _selected.Add(n);
        }
    }

    /// <summary>Enumerates the displays currently attached to the desktop.</summary>
    public IReadOnlyList<MonitorInfo> ListMonitors()
    {
        var list = new List<MonitorInfo>();
        var dd = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                bool primary = (dd.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                string adapter = dd.DeviceName;
                string friendly = adapter;
                var mon = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
                if (NativeMethods.EnumDisplayDevices(adapter, 0, ref mon, 0) && !string.IsNullOrWhiteSpace(mon.DeviceString))
                    friendly = mon.DeviceString;
                list.Add(new MonitorInfo(adapter, friendly, primary));
            }
            dd.cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
        }
        return list;
    }

    /// <summary>
    /// Builds a 768-entry ramp (R,G,B × 256). gamma is clamped to the supported
    /// range. The exponent is 1/gamma, so higher gamma brightens; 1.0 is identity.
    /// </summary>
    public static ushort[] BuildRamp(double gamma)
    {
        gamma = GammaPresets.Clamp(gamma);
        double inv = 1.0 / gamma;
        var ramp = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            double v = gamma == 1.0
                ? i * 65535.0 / 255.0                       // exact identity
                : Math.Pow(i / 255.0, inv) * 65535.0;
            ushort word = (ushort)Math.Clamp(Math.Round(v), 0, 65535);
            ramp[i] = word;        // Red
            ramp[i + 256] = word;  // Green
            ramp[i + 512] = word;  // Blue
        }
        return ramp;
    }

    public enum ApplyResult
    {
        Success,
        ClampedByWindows, // SetDeviceGammaRamp accepted but Windows ignored the curve
        Failed,           // driver rejected the ramp outright
    }

    /// <summary>Applies the given gamma to the configured display target(s).</summary>
    public ApplyResult Apply(double gamma)
    {
        gamma = GammaPresets.Clamp(gamma);
        ushort[] ramp = BuildRamp(gamma);

        bool any = false;
        bool allOk = true;
        bool clamped = false;

        ForEachTarget(hdc =>
        {
            any = true;
            if (!NativeMethods.SetDeviceGammaRamp(hdc, ramp))
            {
                allOk = false;
                return;
            }
            if (!VerifyApplied(hdc, ramp))
                clamped = true;
        });

        lock (_gate)
            _currentGamma = gamma;

        if (!any || !allOk)
            return ApplyResult.Failed;
        return clamped ? ApplyResult.ClampedByWindows : ApplyResult.Success;
    }

    /// <summary>Restores the ramp captured when the app started.</summary>
    public void Restore()
    {
        lock (_gate)
        {
            // Restore every baseline we actually captured...
            foreach (var (name, ramp) in _originals)
                WithDc(name, hdc => NativeMethods.SetDeviceGammaRamp(hdc, ramp));

            // ...and neutralise any current target we have no baseline for, so a
            // monitor we touched is never left stuck on a modified ramp.
            ushort[] identity = BuildRamp(1.0);
            foreach (string name in TargetNames())
                if (!_originals.ContainsKey(name))
                    WithDc(name, hdc => NativeMethods.SetDeviceGammaRamp(hdc, identity));

            _currentGamma = GammaPresets.Default;
        }
    }

    /// <summary>Reapplies the last gamma we set (after a display/power event wiped it).</summary>
    public void Reapply()
    {
        double g;
        lock (_gate) g = _currentGamma;
        if (Math.Abs(g - GammaPresets.Default) > 1e-6)
            Apply(g);
    }

    // ------------------------------------------------------------ internals

    private static bool VerifyApplied(IntPtr hdc, ushort[] desired)
    {
        var actual = new ushort[768];
        if (!NativeMethods.GetDeviceGammaRamp(hdc, actual))
            return true; // can't read back – don't cry wolf
        int[] probes = { 64, 128, 192, 64 + 256, 128 + 256, 192 + 512 };
        foreach (int idx in probes)
            if (Math.Abs(actual[idx] - desired[idx]) > 768)
                return false;
        return true;
    }

    private void CaptureOriginals()
    {
        lock (_gate)
        {
            _originals.Clear();
            foreach (string name in TargetNames())
            {
                WithDc(name, hdc =>
                {
                    var ramp = new ushort[768];
                    if (NativeMethods.GetDeviceGammaRamp(hdc, ramp))
                        _originals[name] = ramp;
                });
            }
        }
    }

    /// <summary>The device names to act on, or a single "" entry meaning the primary DC.</summary>
    private List<string> TargetNames()
    {
        bool all;
        HashSet<string> selected;
        lock (_gate) // reentrant — Restore/CaptureOriginals already hold _gate
        {
            all = _allMonitors;
            selected = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
        }

        var attached = AttachedDisplayNames().ToList();
        if (attached.Count == 0)
            return new List<string> { string.Empty };
        if (all)
            return attached;

        var chosen = attached.Where(a => selected.Contains(a)).ToList();
        return chosen.Count > 0 ? chosen : attached; // stale/empty selection -> all, never nothing
    }

    private void ForEachTarget(Action<IntPtr> action)
    {
        foreach (string name in TargetNames())
            WithDc(name, action);
    }

    /// <summary>Acquires the right kind of DC for a target name and guarantees release.</summary>
    private static void WithDc(string deviceName, Action<IntPtr> action)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return;
            try { action(hdc); }
            finally { NativeMethods.ReleaseDC(IntPtr.Zero, hdc); }
        }
        else
        {
            IntPtr hdc = NativeMethods.CreateDC(null, deviceName, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return;
            try { action(hdc); }
            finally { NativeMethods.DeleteDC(hdc); }
        }
    }

    private static IEnumerable<string> AttachedDisplayNames()
    {
        var dd = new NativeMethods.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                yield return dd.DeviceName;
            dd.cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Topology may have changed; refresh which monitors exist, then reapply.
        Reapply();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Reapply();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Restore();
    }
}
