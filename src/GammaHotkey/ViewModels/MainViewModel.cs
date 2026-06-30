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
            StatusIsSuccess = false;
        };

        ApplyPreviewCommand = new RelayCommand(() => ApplyGamma(PreviewGamma));
        ResetCommand = new RelayCommand(ResetGamma);
        GenerateLuaCommand = new RelayCommand(() => { RegenerateLua(); SetStatus("Script generated", success: true); });
        CopyLuaCommand = new RelayCommand(CopyLua);
        SaveLuaCommand = new RelayCommand(SaveLua);
        AddBindingCommand = new RelayCommand(AddBinding);
        RemoveBindingCommand = new RelayCommand(RemoveBinding);
        RefreshMonitorsCommand = new RelayCommand(RefreshMonitors);
        AddPresetCommand = new RelayCommand(AddPreset);
        RemovePresetCommand = new RelayCommand(RemovePreset);

        LoadFrom(_store.Load());
    }

    // ----------------------------------------------------------- collections

    public ObservableCollection<PresetViewModel> Presets { get; } = new();
    public ObservableCollection<DirectBindingViewModel> DirectBindings { get; } = new();

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

    public ObservableCollection<MonitorOptionViewModel> Monitors { get; } = new();

    private bool _allMonitors = true;
    public bool AllMonitors
    {
        get => _allMonitors;
        set
        {
            if (!SetField(ref _allMonitors, value))
                return;
            OnPropertyChanged(nameof(IndividualMonitorsEnabled));
            OnPropertyChanged(nameof(MonitorsSummary));
            PushTargets();
            Save();
        }
    }

    public bool IndividualMonitorsEnabled => !_allMonitors;

    public string MonitorsSummary
    {
        get
        {
            if (_allMonitors)
                return "All monitors";
            var sel = Monitors.Where(m => m.IsSelected).ToList();
            if (sel.Count == 0)
                return "All monitors";
            if (sel.Count == 1)
                return sel[0].Short;
            return $"{sel.Count} of {Monitors.Count} monitors";
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

    private bool _statusIsSuccess;
    public bool StatusIsSuccess
    {
        get => _statusIsSuccess;
        private set => SetField(ref _statusIsSuccess, value);
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
    public RelayCommand RefreshMonitorsCommand { get; }
    public RelayCommand AddPresetCommand { get; }
    public RelayCommand RemovePresetCommand { get; }

    // ----------------------------------------------------------- load / save

    private void LoadFrom(AppConfig cfg)
    {
        _suppressSave = true;

        foreach (var p in Presets)
            p.PropertyChanged -= OnPresetChanged;
        Presets.Clear();
        foreach (var pc in cfg.Presets)
        {
            var vm = new PresetViewModel(pc.Id, pc.Name, pc.Value, pc.InCycle);
            vm.PropertyChanged += OnPresetChanged;
            Presets.Add(vm);
        }

        foreach (var b in DirectBindings)
            b.PropertyChanged -= OnDirectChanged;
        DirectBindings.Clear();
        foreach (var dc in cfg.Direct)
        {
            var vm = new DirectBindingViewModel(dc.Trigger, dc.PresetId);
            vm.PropertyChanged += OnDirectChanged;
            DirectBindings.Add(vm);
        }

        _mode = cfg.Mode;
        _cycleTrigger = cfg.Cycle.Trigger;
        _cycleWrap = cfg.Cycle.Wrap;
        _allMonitors = cfg.ApplyToAllMonitors;
        RefreshMonitors();
        if (cfg.SelectedMonitors.Count > 0)
        {
            var savedSel = new HashSet<string>(cfg.SelectedMonitors, StringComparer.OrdinalIgnoreCase);
            foreach (var m in Monitors)
                m.IsSelected = savedSel.Contains(m.DeviceName);
        }
        PushTargets();
        SwallowMouse = cfg.SwallowMouse;
        _runOnStartup = StartupManager.IsEnabled();
        _isListening = cfg.Listening;
        _hooks.IsListening = cfg.Listening;

        OnPropertyChanged(nameof(IsCycleMode));
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(CycleTrigger));
        OnPropertyChanged(nameof(AllMonitors));
        OnPropertyChanged(nameof(IndividualMonitorsEnabled));
        OnPropertyChanged(nameof(MonitorsSummary));
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
            ApplyToAllMonitors = _allMonitors,
            SelectedMonitors = Monitors.Where(m => m.IsSelected).Select(m => m.DeviceName).ToList(),
            RunOnStartup = _runOnStartup,
            Listening = _isListening,
        };

        foreach (var p in Presets)
            cfg.Presets.Add(new PresetConfig { Id = p.Id, Name = p.Name, Value = p.Value, InCycle = p.IsInCycle });

        cfg.Cycle = new CycleConfig { Trigger = _cycleTrigger, Wrap = _cycleWrap };

        foreach (var b in DirectBindings)
            cfg.Direct.Add(new DirectBindingConfig { Trigger = b.Trigger, PresetId = b.PresetId });

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

    // ----------------------------------------------------------- monitors

    /// <summary>Re-enumerates attached displays, preserving the current selection.</summary>
    public void RefreshMonitors()
    {
        var prevSelected = new HashSet<string>(
            Monitors.Where(m => m.IsSelected).Select(m => m.DeviceName), StringComparer.OrdinalIgnoreCase);
        bool hadList = Monitors.Count > 0;

        foreach (var m in Monitors)
            m.PropertyChanged -= OnMonitorChanged;
        Monitors.Clear();

        foreach (var mon in _gamma.ListMonitors())
        {
            string shortName = mon.DeviceName.Replace(@"\\.\", string.Empty);
            string primaryTag = mon.IsPrimary ? "  (primary)" : string.Empty;
            string label = $"{shortName} — {mon.FriendlyName}{primaryTag}";
            string shortLabel = $"{shortName}{primaryTag}";
            bool selected = !hadList || prevSelected.Contains(mon.DeviceName);
            var vm = new MonitorOptionViewModel(mon.DeviceName, label, shortLabel, mon.IsPrimary, selected);
            vm.PropertyChanged += OnMonitorChanged;
            Monitors.Add(vm);
        }
        OnPropertyChanged(nameof(MonitorsSummary));
    }

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MonitorOptionViewModel.IsSelected))
            return;
        PushTargets();
        OnPropertyChanged(nameof(MonitorsSummary));
        Save();
    }

    private void PushTargets()
    {
        var selected = Monitors.Where(m => m.IsSelected).Select(m => m.DeviceName).ToList();
        bool all = _allMonitors || selected.Count == 0;
        _gamma.SetTargets(all, selected);
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
            var preset = Presets.FirstOrDefault(p => p.Id == binding.PresetId);
            if (preset != null)
                ApplyPreset(preset);
        }
    }

    private void AdvanceCycle()
    {
        var steps = Presets.Where(p => p.IsInCycle).ToList();
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
                SetStatus($"Gamma {shown}", success: true);
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
        SetStatus("Reset to 1.00", success: true);
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
            SetStatus("Script copied", success: true);
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
                SetStatus($"Saved {Path.GetFileName(dlg.FileName)}", success: true);
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
        string presetId = Presets.FirstOrDefault()?.Id ?? string.Empty;
        var vm = new DirectBindingViewModel(TriggerInput.None, presetId);
        vm.PropertyChanged += OnDirectChanged;
        DirectBindings.Add(vm);
        OnConfigChanged();
    }

    private void AddPreset(object? _)
    {
        var vm = new PresetViewModel(Guid.NewGuid().ToString("N"), $"Preset {Presets.Count + 1}", 1.20, false);
        vm.PropertyChanged += OnPresetChanged;
        Presets.Add(vm);
        OnConfigChanged();
    }

    private void RemovePreset(object? parameter)
    {
        if (parameter is not PresetViewModel preset || Presets.Count <= 1)
            return;

        preset.PropertyChanged -= OnPresetChanged;
        Presets.Remove(preset);

        // Repoint any direct bindings that referenced it to the first remaining preset.
        string fallback = Presets[0].Id;
        foreach (var b in DirectBindings.Where(b => b.PresetId == preset.Id).ToList())
            b.PresetId = fallback;

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

    private void SetStatus(string text, bool warning = false, bool success = false)
    {
        Status = text;
        StatusIsWarning = warning;
        StatusIsSuccess = success;
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
