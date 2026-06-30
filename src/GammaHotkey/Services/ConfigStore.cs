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
                if (cfg != null && cfg.Version >= AppConfig.CurrentVersion)
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

    /// <summary>Repairs ids/names and drops dangling references (handles partial files).</summary>
    private static AppConfig Normalize(AppConfig cfg)
    {
        cfg.Presets ??= new List<PresetConfig>();
        cfg.Cycle ??= new CycleConfig();
        cfg.Direct ??= new List<DirectBindingConfig>();
        cfg.SelectedMonitors ??= new List<string>();

        var seenIds = new HashSet<string>();
        foreach (var p in cfg.Presets)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !seenIds.Add(p.Id))
            {
                p.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(p.Id);
            }
            if (string.IsNullOrWhiteSpace(p.Name))
                p.Name = "Preset";
            p.Value = GammaPresets.Clamp(p.Value);
        }

        if (cfg.Presets.Count == 0)
            cfg.Presets = AppConfig.CreateDefault().Presets;

        // Drop direct bindings that point at a preset that no longer exists.
        var ids = new HashSet<string>(cfg.Presets.Select(p => p.Id));
        cfg.Direct = cfg.Direct.Where(b => ids.Contains(b.PresetId)).ToList();

        return cfg;
    }
}
