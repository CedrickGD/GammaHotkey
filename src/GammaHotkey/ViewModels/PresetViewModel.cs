using GammaHotkey.Models;
using GammaHotkey.Mvvm;

namespace GammaHotkey.ViewModels;

/// <summary>One editable gamma preset: a stable id, an editable name + value, and
/// whether it is included in the cycle.</summary>
public sealed class PresetViewModel : ObservableObject
{
    public string Id { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? _name : value.Trim());
    }

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

    public PresetViewModel(string id, string name, double value, bool isInCycle)
    {
        Id = id;
        _name = name;
        _value = GammaPresets.Clamp(value);
        _isInCycle = isInCycle;
    }
}
