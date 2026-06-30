using GammaHotkey.Models;
using GammaHotkey.Mvvm;

namespace GammaHotkey.ViewModels;

/// <summary>One "press this key/button -> jump to this preset" row in Direct mode.</summary>
public sealed class DirectBindingViewModel : ObservableObject
{
    private TriggerInput _trigger;
    public TriggerInput Trigger
    {
        get => _trigger;
        set
        {
            if (SetField(ref _trigger, value))
                OnPropertyChanged(nameof(TriggerDisplay));
        }
    }

    public string TriggerDisplay => _trigger.Describe();

    private string _presetId;
    public string PresetId
    {
        get => _presetId;
        set => SetField(ref _presetId, value);
    }

    public DirectBindingViewModel(TriggerInput trigger, string presetId)
    {
        _trigger = trigger;
        _presetId = presetId;
    }
}
