using GammaHotkey.Models;
using GammaHotkey.Mvvm;

namespace GammaHotkey.ViewModels;

/// <summary>One editable named gamma preset (Low … Max) plus its "in cycle" flag.</summary>
public sealed class PresetViewModel : ObservableObject
{
    public GammaLevel Level { get; }

    public string Name => GammaPresets.DisplayName(Level);

    private double _value;
    public double Value
    {
        get => _value;
        set => SetField(ref _value, GammaPresets.Clamp(value));
    }

    private bool _isInCycle;
    public bool IsInCycle
    {
        get => _isInCycle;
        set => SetField(ref _isInCycle, value);
    }

    public PresetViewModel(GammaLevel level, double value, bool isInCycle)
    {
        Level = level;
        _value = GammaPresets.Clamp(value);
        _isInCycle = isInCycle;
    }
}
