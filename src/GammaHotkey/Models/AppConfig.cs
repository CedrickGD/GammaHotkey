using System.Text.Json;
using System.Text.Json.Serialization;
using GammaHotkey.Services;

namespace GammaHotkey.Models;

/// <summary>Which trigger style is currently active.</summary>
public enum TriggerMode
{
    Cycle,
    Direct,
}

/// <summary>One user preset: a stable id, an editable name, a value, and whether
/// it is part of the cycle.</summary>
public sealed class PresetConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; } = 1.0;
    public bool InCycle { get; set; }
}

public sealed class CycleConfig
{
    public TriggerInput Trigger { get; set; } = TriggerInput.None;
    public bool Wrap { get; set; } = true;
}

public sealed class DirectBindingConfig
{
    public TriggerInput Trigger { get; set; } = TriggerInput.None;
    public string PresetId { get; set; } = string.Empty;
}

/// <summary>Root persisted configuration (written to %APPDATA%\GammaHotkey\config.json).</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<PresetConfig> Presets { get; set; } = new();
    public TriggerMode Mode { get; set; } = TriggerMode.Cycle;
    public CycleConfig Cycle { get; set; } = new();
    public List<DirectBindingConfig> Direct { get; set; } = new();
    public bool SwallowMouse { get; set; } = true;
    public bool ApplyToAllMonitors { get; set; } = true;
    public List<string> SelectedMonitors { get; set; } = new();
    public bool RunOnStartup { get; set; }

    /// <summary>Whether to start listening for triggers automatically on launch.</summary>
    public bool Listening { get; set; }

    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig { Version = CurrentVersion };

        var ids = new Dictionary<GammaLevel, string>();
        foreach (var level in GammaPresets.AllLevels)
        {
            string id = Guid.NewGuid().ToString("N");
            ids[level] = id;
            bool inCycle = level is GammaLevel.Normal or GammaLevel.Higher or GammaLevel.High or GammaLevel.Max;
            cfg.Presets.Add(new PresetConfig
            {
                Id = id,
                Name = GammaPresets.DisplayName(level),
                Value = GammaPresets.DefaultValue(level),
                InCycle = inCycle,
            });
        }

        // Out of the box: F13 cycles Normal -> Higher -> High -> Max.
        cfg.Cycle.Trigger = TriggerInput.Key(KeyNames.VK_F13);

        // ...and a couple of direct examples on F14 / F15.
        cfg.Direct.Add(new DirectBindingConfig { Trigger = TriggerInput.Key(KeyNames.VK_F13 + 1), PresetId = ids[GammaLevel.Normal] });
        cfg.Direct.Add(new DirectBindingConfig { Trigger = TriggerInput.Key(KeyNames.VK_F13 + 2), PresetId = ids[GammaLevel.Max] });

        return cfg;
    }

    public static JsonSerializerOptions JsonOptions { get; } = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        opts.Converters.Add(new TriggerInputJsonConverter());
        return opts;
    }
}

/// <summary>
/// Serializes a <see cref="TriggerInput"/> as a tagged object:
/// <c>{ "kind": "keyboard", "vk": 124 }</c> or <c>{ "kind": "mouse", "button": "XButton1" }</c>.
/// </summary>
public sealed class TriggerInputJsonConverter : JsonConverter<TriggerInput>
{
    public override TriggerInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return TriggerInput.None;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected an object for TriggerInput.");

        string kind = "keyboard";
        int vk = 0;
        MouseButton button = MouseButton.XButton1;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string prop = reader.GetString() ?? string.Empty;
            reader.Read();
            switch (prop.ToLowerInvariant())
            {
                case "kind":
                    kind = reader.GetString() ?? "keyboard";
                    break;
                case "vk":
                    vk = reader.GetInt32();
                    break;
                case "button":
                    Enum.TryParse(reader.GetString(), ignoreCase: true, out button);
                    break;
            }
        }

        return kind.Equals("mouse", StringComparison.OrdinalIgnoreCase)
            ? TriggerInput.Mouse(button)
            : TriggerInput.Key(vk);
    }

    public override void Write(Utf8JsonWriter writer, TriggerInput value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Kind == TriggerKind.Mouse)
        {
            writer.WriteString("kind", "mouse");
            writer.WriteString("button", value.Button.ToString());
        }
        else
        {
            writer.WriteString("kind", "keyboard");
            writer.WriteNumber("vk", value.VirtualKey);
        }
        writer.WriteEndObject();
    }
}
