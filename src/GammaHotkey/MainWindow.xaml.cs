using System.Windows;
using GammaHotkey.Services;
using GammaHotkey.ViewModels;

namespace GammaHotkey;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnStateChanged;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Minimizing tucks the window into the tray instead of the taskbar.
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void MonitorsButton_Click(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.RefreshMonitors(); // re-detect on open (handles hot-plug)
        MonitorsPopup.IsOpen = true;
    }

    private void ExpandScript_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.GenerateLuaCommand.Execute(null); // make sure the preview is current
        var win = new ScriptWindow { Owner = this, DataContext = DataContext };
        win.Show();
    }
}
