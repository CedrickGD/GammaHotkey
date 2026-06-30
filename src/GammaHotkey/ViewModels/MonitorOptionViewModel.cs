using GammaHotkey.Mvvm;

namespace GammaHotkey.ViewModels;

/// <summary>One selectable display in the "Monitors" picker.</summary>
public sealed class MonitorOptionViewModel : ObservableObject
{
    public string DeviceName { get; }
    public string Display { get; }   // full label, shown in the popup
    public string Short { get; }     // compact label, shown on the footer button
    public bool IsPrimary { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public MonitorOptionViewModel(string deviceName, string display, string shortLabel, bool isPrimary, bool isSelected)
    {
        DeviceName = deviceName;
        Display = display;
        Short = shortLabel;
        IsPrimary = isPrimary;
        _isSelected = isSelected;
    }
}
