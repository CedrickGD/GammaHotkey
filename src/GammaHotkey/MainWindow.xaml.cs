using System.Windows;

namespace GammaHotkey;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Minimizing tucks the window into the tray instead of the taskbar.
        if (WindowState == WindowState.Minimized)
            Hide();
    }
}
