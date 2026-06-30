using System.IO;
using System.Text.Json;
using GammaHotkey.Models;

namespace GammaHotkey.Services;

/// <summary>Loads / saves <see cref="AppConfig"/> to %APPDATA%\GammaHotkey\config.json.</summary>
public sealed class ConfigStore
{
    private readonly string _dir;
    private readonly string _path;

    public ConfigStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GammaHotkey");
        _path = Path.Combine(_dir, "config.json");
    }

    public string FilePath => _path;

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, AppConfig.JsonOptions);
                if (cfg != null)
                    return Normalize(cfg);
            }
        }
        catch
        {
            // Corrupt or unreadable config – fall back to defaults rather than crash.
        }
        return AppConfig.CreateDefault();
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            string json = JsonSerializer.Serialize(config, AppConfig.JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Saving config is best-effort; never take the app down over it.
        }
    }

    /// <summary>Guarantees every named preset exists exactly once (handles older / partial files).</summary>
    private static AppConfig Normalize(AppConfig cfg)
    {
        var byLevel = cfg.Presets
            .GroupBy(p => p.Level)
            .ToDictionary(g => g.Key, g => g.First().Value);

        cfg.Presets = GammaPresets.AllLevels
            .Select(level => new PresetConfig
            {
                Level = level,
                Value = GammaPresets.Clamp(byLevel.TryGetValue(level, out var v) ? v : GammaPresets.DefaultValue(level)),
            })
            .ToList();

        cfg.Cycle ??= new CycleConfig();
        cfg.Direct ??= new List<DirectBindingConfig>();
        if (cfg.Cycle.Steps.Count == 0)
            cfg.Cycle.Steps = new List<GammaLevel> { GammaLevel.Normal, GammaLevel.High };
        return cfg;
    }
}
