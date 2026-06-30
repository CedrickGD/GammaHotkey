using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GammaHotkey.Models;
using GammaHotkey.Mvvm;
using GammaHotkey.Services;
using Microsoft.Win32;

namespace GammaHotkey.ViewModels;

/// <summary>A selectable preset in the Direct-binding combo box.</summary>
public sealed record LevelOption(GammaLevel Level, string Name);

public sealed class MainViewModel : ObservableObject
{
    private readonly GammaController _gamma;
    private readonly HookService _hooks;
    private readonly ConfigStore _store;
    private readonly DispatcherTimer _statusTimer;

    private bool _suppressSave = true;
    private TriggerMode _mode = TriggerMode.Cycle;
    private bool _cycleWrap = true;
    private int _cycleIndex = -1;

    public MainViewModel(GammaController gamma, HookService hooks, ConfigStore store)
    {
        _gamma = gamma;
        _hooks = hooks;
        _store = store;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            Status = IsListening ? $"Listening{ActiveTriggerSummary()}" : "Idle";
            StatusIsWarning = false;
        };

        ApplyPreviewCommand = new RelayCommand(() => ApplyGamma(PreviewGamma));
        ResetCommand = new RelayCommand(ResetGamma);
        GenerateLuaCommand = new RelayCommand(() => { RegenerateLua(); SetStatus("Script generated"); });
        CopyLuaCommand = new RelayCommand(CopyLua);
        SaveLuaCommand = new RelayCommand(SaveLua);
        AddBindingCommand = new RelayCommand(AddBinding);
        RemoveBindingCommand = new RelayCommand(RemoveBinding);

        LoadFrom(_store.Load());
    }

    // ----------------------------------------------------------- collections

    public ObservableCollection<PresetViewModel> Presets { get; } = new();
    public ObservableCollection<DirectBindingViewModel> DirectBindings { get; } = new();

    public IReadOnlyList<LevelOption> LevelOptions { get; } =
        GammaPresets.AllLevels.Select(l => new LevelOption(l, GammaPresets.DisplayName(l))).ToList();

    // ----------------------------------------------------------- trigger mode

    public bool IsCycleMode
    {
        get => _mode == TriggerMode.Cycle;
        set { if (value) SetMode(TriggerMode.Cycle); }
    }

    public bool IsDirectMode
    {
        get => _mode == TriggerMode.Direct;
        set { if (value) SetMode(TriggerMode.Direct); }
    }

    private void SetMode(TriggerMode mode)
    {
        if (_mode == mode)
            return;
        _mode = mode;
        _cycleIndex = -1; // switching modes restarts the cycle
        OnPropertyChanged(nameof(IsCycleMode));
        OnPropertyChanged(nameof(IsDirectMode));
        OnConfigChanged();
    }

    private TriggerInput _cycleTrigger = TriggerInput.None;
    public TriggerInput CycleTrigger
    {
        get => _cycleTrigger;
        set { if (SetField(ref _cycleTrigger, value)) { _cycleIndex = -1; OnConfigChanged(); } }
    }

    // ----------------------------------------------------------- live preview

    private double _previewGamma = GammaPresets.Default;
    public double PreviewGamma
    {
        get => _previewGamma;
        set
        {
            double clamped = GammaPresets.Clamp(value);
            if (Math.Abs(clamped - _previewGamma) < 1e-9)
                return;
            SetPreviewField(clamped);
            ApplyGamma(clamped); // live-apply so the user sees it instantly
        }
    }

    public string PreviewGammaText => _previewGamma.ToString("0.00", CultureInfo.InvariantCulture);

    private void SetPreviewField(double value)
    {
        _previewGamma = value;
        OnPropertyChanged(nameof(PreviewGamma));
        OnPropertyChanged(nameof(PreviewGammaText));
    }

    // ----------------------------------------------------------- toggles

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (!SetField(ref _isListening, value))
                return;
            _hooks.IsListening = value;
            _cycleIndex = -1;
            RefreshBound();
            OnPropertyChanged(nameof(ListeningLabel));
            SetStatus(value ? $"Listening{ActiveTriggerSummary()}" : "Idle");
            Save();
        }
    }

    public string ListeningLabel => IsListening ? "Listening" : "Start listening";

    private bool _runOnStartup;
    public bool RunOnStartup
    {
        get => _runOnStartup;
        set
        {
            if (!SetField(ref _runOnStartup, value))
                return;
            StartupManager.SetEnabled(value);
            Save();
        }
    }

    private bool _applyToAllMonitors = true;
    public bool ApplyToAllMonitors
    {
        get => _applyToAllMonitors;
        set
        {
            if (!SetField(ref _applyToAllMonitors, value))
                return;
            _gamma.ApplyToAllMonitors = value;
            Save();
        }
    }

    public bool SwallowMouse { get; private set; } = true;

    // ----------------------------------------------------------- status + lua

    private string _status = "Idle";
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private bool _statusIsWarning;
    public bool StatusIsWarning
    {
        get => _statusIsWarning;
        private set => SetField(ref _statusIsWarning, value);
    }

    private string _luaScript = string.Empty;
    public string LuaScript
    {
        get => _luaScript;
        private set => SetField(ref _luaScript, value);
    }

    public string ConfigPath => _store.FilePath;

    // ----------------------------------------------------------- commands

    public RelayCommand ApplyPreviewCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand GenerateLuaCommand { get; }
    public RelayCommand CopyLuaCommand { get; }
    public RelayCommand SaveLuaCommand { get; }
    public RelayCommand AddBindingCommand { get; }
    public RelayCommand RemoveBindingCommand { get; }

    // ----------------------------------------------------------- load / save

    private void LoadFrom(AppConfig cfg)
    {
        _suppressSave = true;

        foreach (var p in Presets)
            p.PropertyChanged -= OnPresetChanged;
        Presets.Clear();
        foreach (var pc in cfg.Presets)
        {
            var vm = new PresetViewModel(pc.Level, pc.Value, cfg.Cycle.Steps.Contains(pc.Level));
            vm.PropertyChanged += OnPresetChanged;
            Presets.Add(vm);
        }

        foreach (var b in DirectBindings)
            b.PropertyChanged -= OnDirectChanged;
        DirectBindings.Clear();
        foreach (var dc in cfg.Direct)
        {
            var vm = new DirectBindingViewModel(dc.Trigger, dc.Level);
            vm.PropertyChanged += OnDirectChanged;
            DirectBindings.Add(vm);
        }

        _mode = cfg.Mode;
        _cycleTrigger = cfg.Cycle.Trigger;
        _cycleWrap = cfg.Cycle.Wrap;
        _applyToAllMonitors = cfg.ApplyToAllMonitors;
        _gamma.ApplyToAllMonitors = cfg.ApplyToAllMonitors;
        SwallowMouse = cfg.SwallowMouse;
        _runOnStartup = StartupManager.IsEnabled();
        _isListening = cfg.Listening;
        _hooks.IsListening = cfg.Listening;

        OnPropertyChanged(nameof(IsCycleMode));
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(CycleTrigger));
        OnPropertyChanged(nameof(ApplyToAllMonitors));
        OnPropertyChanged(nameof(RunOnStartup));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(ListeningLabel));

        _suppressSave = false;

        RefreshBound();
        RegenerateLua();
        Status = _isListening ? $"Listening{ActiveTriggerSummary()}" : "Idle";
    }

    public AppConfig ToConfig()
    {
        var cfg = new AppConfig
        {
            Version = 1,
            Mode = _mode,
            SwallowMouse = SwallowMouse,
            ApplyToAllMonitors = _applyToAllMonitors,
            RunOnStartup = _runOnStartup,
            Listening = _isListening,
        };

        foreach (var p in Presets)
            cfg.Presets.Add(new PresetConfig { Level = p.Level, Value = p.Value });

        cfg.Cycle = new CycleConfig
        {
            Trigger = _cycleTrigger,
            Wrap = _cycleWrap,
            Steps = Presets.Where(p => p.IsInCycle)
                           .OrderBy(p => (int)p.Level)
                           .Select(p => p.Level)
                           .ToList(),
        };

        foreach (var b in DirectBindings)
            cfg.Direct.Add(new DirectBindingConfig { Trigger = b.Trigger, Level = b.SelectedLevel });

        return cfg;
    }

    public void Save()
    {
        if (_suppressSave)
            return;
        _store.Save(ToConfig());
    }

    private void OnConfigChanged()
    {
        if (_suppressSave)
            return;
        RefreshBound();
        RegenerateLua();
        Save();
    }

    private void OnPresetChanged(object? sender, PropertyChangedEventArgs e) => OnConfigChanged();
    private void OnDirectChanged(object? sender, PropertyChangedEventArgs e) => OnConfigChanged();

    // ----------------------------------------------------------- binding set

    /// <summary>Pushes the active-mode trigger set to the hook service.</summary>
    private void RefreshBound()
    {
        IEnumerable<TriggerInput> active = _mode == TriggerMode.Cycle
            ? (_cycleTrigger.IsEmpty ? Array.Empty<TriggerInput>() : new[] { _cycleTrigger })
            : DirectBindings.Select(b => b.Trigger).Where(t => !t.IsEmpty);

        _hooks.UpdateBindings(active, SwallowMouse);
    }

    // ----------------------------------------------------------- dispatch

    /// <summary>Called (on the UI thread) when a bound trigger fires while listening.</summary>
    public void HandleTrigger(TriggerInput t)
    {
        if (!IsListening)
            return;

        if (_mode == TriggerMode.Cycle)
        {
            if (t != _cycleTrigger)
                return;
            AdvanceCycle();
        }
        else
        {
            var binding = DirectBindings.FirstOrDefault(b => b.Trigger == t);
            if (binding == null)
                return;
            var preset = Presets.FirstOrDefault(p => p.Level == binding.SelectedLevel);
            if (preset != null)
                ApplyPreset(preset);
        }
    }

    private void AdvanceCycle()
    {
        var steps = Presets.Where(p => p.IsInCycle).OrderBy(p => (int)p.Level).ToList();
        if (steps.Count == 0)
        {
            SetStatus("No presets in the cycle", warning: true);
            return;
        }

        _cycleIndex++;
        if (_cycleIndex >= steps.Count)
            _cycleIndex = _cycleWrap ? 0 : steps.Count - 1;

        ApplyPreset(steps[_cycleIndex]);
    }

    private void ApplyPreset(PresetViewModel preset)
    {
        SetPreviewField(preset.Value);
        ApplyGamma(preset.Value, $"{preset.Name} ({preset.Value:0.00})");
    }

    // ----------------------------------------------------------- gamma actions

    private void ApplyGamma(double gamma, string? label = null)
    {
        var result = _gamma.Apply(gamma);
        string shown = label ?? gamma.ToString("0.00", CultureInfo.InvariantCulture);
        switch (result)
        {
            case GammaController.ApplyResult.Success:
                SetStatus($"Gamma {shown}");
                break;
            case GammaController.ApplyResult.ClampedByWindows:
                SetStatus($"Gamma {shown} — Windows limited the range (see README)", warning: true);
                break;
            default:
                SetStatus("Couldn't set gamma — turn HDR off, or you may be on a remote/virtual display (see README)", warning: true);
                break;
        }
    }

    private void ResetGamma()
    {
        SetPreviewField(GammaPresets.Default);
        _gamma.Apply(GammaPresets.Default);
        _cycleIndex = -1;
        SetStatus("Reset to 1.00");
    }

    // ----------------------------------------------------------- lua actions

    private void RegenerateLua() => LuaScript = LuaGenerator.Generate(ToConfig());

    private void CopyLua()
    {
        if (string.IsNullOrWhiteSpace(LuaScript))
            RegenerateLua();
        try
        {
            Clipboard.SetText(LuaScript);
            SetStatus("Script copied");
        }
        catch
        {
            SetStatus("Couldn't access the clipboard", warning: true);
        }
    }

    private void SaveLua()
    {
        if (string.IsNullOrWhiteSpace(LuaScript))
            RegenerateLua();

        var dlg = new SaveFileDialog
        {
            Title = "Save G HUB Lua script",
            FileName = "gammahotkey.lua",
            DefaultExt = ".lua",
            Filter = "Lua script (*.lua)|*.lua|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, LuaScript);
                SetStatus($"Saved {Path.GetFileName(dlg.FileName)}");
            }
            catch
            {
                SetStatus("Couldn't save the file", warning: true);
            }
        }
    }

    // ----------------------------------------------------------- bindings list

    private void AddBinding(object? _)
    {
        var vm = new DirectBindingViewModel(TriggerInput.None, GammaLevel.Normal);
        vm.PropertyChanged += OnDirectChanged;
        DirectBindings.Add(vm);
        OnConfigChanged();
    }

    private void RemoveBinding(object? parameter)
    {
        if (parameter is DirectBindingViewModel vm)
        {
            vm.PropertyChanged -= OnDirectChanged;
            DirectBindings.Remove(vm);
            OnConfigChanged();
        }
    }

    // ----------------------------------------------------------- status helper

    private void SetStatus(string text, bool warning = false)
    {
        Status = text;
        StatusIsWarning = warning;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private string ActiveTriggerSummary()
    {
        if (_mode == TriggerMode.Cycle)
            return _cycleTrigger.IsEmpty ? string.Empty : $" — {_cycleTrigger.Describe()} cycles gamma";
        int n = DirectBindings.Count(b => !b.Trigger.IsEmpty);
        return n == 0 ? string.Empty : $" — {n} binding{(n == 1 ? "" : "s")} active";
    }
}
