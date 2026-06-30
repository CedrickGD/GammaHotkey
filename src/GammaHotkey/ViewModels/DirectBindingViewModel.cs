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

    private GammaLevel _selectedLevel;
    public GammaLevel SelectedLevel
    {
        get => _selectedLevel;
        set => SetField(ref _selectedLevel, value);
    }

    public DirectBindingViewModel(TriggerInput trigger, GammaLevel level)
    {
        _trigger = trigger;
        _selectedLevel = level;
    }
}
