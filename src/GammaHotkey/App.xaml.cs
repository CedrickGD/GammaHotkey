using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using GammaHotkey.Services;
using GammaHotkey.ViewModels;
using Drawing = System.Drawing;
using WF = System.Windows.Forms;

namespace GammaHotkey;

public partial class App : Application
{
    private Mutex? _mutex;
    private GammaController? _gamma;
    private HookService? _hooks;
    private ConfigStore? _store;
    private MainViewModel? _vm;
    private MainWindow? _window;

    private WF.NotifyIcon? _notifyIcon;
    private WF.ToolStripMenuItem? _listenItem;
    private WF.ToolStripMenuItem? _startupItem;

    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, @"Global\GammaHotkey.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Another instance already owns the gamma + hooks; bow out quietly.
            Shutdown();
            return;
        }

        // Make sure a crash / sign-out never leaves the screen on a weird ramp.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => SafeRestoreGamma();
        SessionEnding += (_, _) => SafeRestoreGamma();

        _gamma = new GammaController();
        _hooks = new HookService(Dispatcher);
        InputCapture.Service = _hooks;
        _store = new ConfigStore();

        _vm = new MainViewModel(_gamma, _hooks, _store);
        _hooks.TriggerFired += _vm.HandleTrigger;
        _hooks.Start();

        _window = new MainWindow { DataContext = _vm };
        _window.Closing += OnWindowClosing;

        CreateTrayIcon();
        _vm.PropertyChanged += OnVmPropertyChanged;
        SyncTrayChecks();

        bool startHidden = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (!startHidden)
            _window.Show();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
            return;
        // Closing the window just hides it; the app keeps listening from the tray.
        e.Cancel = true;
        _window?.Hide();
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new WF.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "GammaHotkey",
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(new WF.ToolStripMenuItem("Settings…", null, (_, _) => ShowWindow()));

        _listenItem = new WF.ToolStripMenuItem("Listening", null, (_, _) =>
        {
            if (_vm != null) _vm.IsListening = !_vm.IsListening;
        });
        menu.Items.Add(_listenItem);

        _startupItem = new WF.ToolStripMenuItem("Run on startup", null, (_, _) =>
        {
            if (_vm != null) _vm.RunOnStartup = !_vm.RunOnStartup;
        });
        menu.Items.Add(_startupItem);

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(new WF.ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        _notifyIcon.ContextMenuStrip = menu;
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null)
                return new Drawing.Icon(info.Stream);
        }
        catch { /* fall through */ }

        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon != null)
                    return icon;
            }
        }
        catch { /* fall through */ }

        return Drawing.SystemIcons.Application;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsListening) or nameof(MainViewModel.RunOnStartup))
            SyncTrayChecks();
    }

    private void SyncTrayChecks()
    {
        if (_vm == null)
            return;
        if (_listenItem != null)
            _listenItem.Checked = _vm.IsListening;
        if (_startupItem != null)
            _startupItem.Checked = _vm.RunOnStartup;
    }

    private void ShowWindow()
    {
        if (_window == null)
            return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void ExitApp()
    {
        _exiting = true;
        try { _hooks?.Stop(); } catch { }
        try { _gamma?.Dispose(); } catch { } // restores the original ramp
        try
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
        catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        Shutdown();
    }

    private void SafeRestoreGamma()
    {
        try { _gamma?.Restore(); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _gamma?.Restore(); } catch { }
        try { _notifyIcon?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
