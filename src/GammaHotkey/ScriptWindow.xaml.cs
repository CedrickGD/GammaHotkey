using System.Windows;
using GammaHotkey.Services;

namespace GammaHotkey;

public partial class ScriptWindow : Window
{
    public ScriptWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
