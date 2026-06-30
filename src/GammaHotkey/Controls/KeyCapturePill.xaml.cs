using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GammaHotkey.Models;
using GammaHotkey.Services;

namespace GammaHotkey.Controls;

/// <summary>
/// A pill that shows the bound trigger and lets the user (a) click to arm a one-shot
/// capture of any key / mouse side-button, or (b) pick an F13–F24 key from a dropdown
/// for use with a G HUB Lua script.
/// </summary>
public partial class KeyCapturePill : UserControl
{
    private static KeyCapturePill? _active;
    private bool _armed;

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger), typeof(TriggerInput), typeof(KeyCapturePill),
        new FrameworkPropertyMetadata(TriggerInput.None,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTriggerChanged));

    public TriggerInput Trigger
    {
        get => (TriggerInput)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    public KeyCapturePill()
    {
        InitializeComponent();
        KeyList.ItemsSource = KeyNames.GHubKeyOptions;
        Loaded += (_, _) => UpdateVisual();
        Unloaded += (_, _) => { if (_active == this) CancelArm(); };
    }

    private static void OnTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((KeyCapturePill)d).UpdateVisual();

    private void ArmArea_Click(object sender, MouseButtonEventArgs e)
    {
        if (_armed)
            CancelArm();
        else
            Arm();
    }

    private void Arm()
    {
        var svc = InputCapture.Service;
        if (svc == null)
            return;

        if (_active != null && _active != this)
            _active.CancelArm();

        _active = this;
        _armed = true;
        UpdateVisual();
        svc.BeginCapture(OnCaptured, OnCancelled);
    }

    private void CancelArm()
    {
        if (_armed)
            InputCapture.Service?.CancelCapture();
        _armed = false;
        if (_active == this)
            _active = null;
        UpdateVisual();
    }

    private void OnCaptured(TriggerInput t)
    {
        _armed = false;
        if (_active == this)
            _active = null;
        Trigger = t; // raises OnTriggerChanged -> UpdateVisual + binding back to VM
    }

    private void OnCancelled()
    {
        _armed = false;
        if (_active == this)
            _active = null;
        UpdateVisual();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_armed)
            CancelArm();
        Trigger = TriggerInput.None;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_armed)
            CancelArm();
        KeyPopup.IsOpen = true;
    }

    private void KeyOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GhubKeyOption option })
        {
            Trigger = TriggerInput.Key(option.VirtualKey);
            KeyPopup.IsOpen = false;
        }
    }

    private void UpdateVisual()
    {
        Brush accent = Brush("Accent", Brushes.Cyan);
        Brush border = Brush("Border", Brushes.Gray);
        Brush textPrimary = Brush("TextPrimary", Brushes.White);
        Brush textDisabled = Brush("TextDisabled", Brushes.Gray);

        if (_armed)
        {
            Pill.BorderBrush = accent;
            DisplayTextBlock.Text = "Listening for input…  (Esc cancels)";
            DisplayTextBlock.Foreground = accent;
            ClearButton.Visibility = Visibility.Collapsed;
            return;
        }

        Pill.BorderBrush = border;
        if (Trigger.IsEmpty)
        {
            DisplayTextBlock.Text = "Press a key or mouse button…";
            DisplayTextBlock.Foreground = textDisabled;
            ClearButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            DisplayTextBlock.Text = Trigger.Describe();
            DisplayTextBlock.Foreground = textPrimary;
            ClearButton.Visibility = Visibility.Visible;
        }
    }

    private Brush Brush(string key, Brush fallback)
        => TryFindResource(key) as Brush ?? fallback;
}
