using GammaHotkey.Mvvm;

namespace GammaHotkey.ViewModels;

/// <summary>One selectable display in the "Monitors" picker.</summary>
public sealed class MonitorOptionViewModel : ObservableObject
{
    public string DeviceName { get; }
    public string Display { get; }
    public bool IsPrimary { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public MonitorOptionViewModel(string deviceName, string display, bool isPrimary, bool isSelected)
    {
        DeviceName = deviceName;
        Display = display;
        IsPrimary = isPrimary;
        _isSelected = isSelected;
    }
}
