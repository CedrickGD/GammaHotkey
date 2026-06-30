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

public sealed class PresetConfig
{
    public GammaLevel Level { get; set; }
    public double Value { get; set; }
}

public sealed class CycleConfig
{
    public TriggerInput Trigger { get; set; } = TriggerInput.None;
    public List<GammaLevel> Steps { get; set; } = new();
    public bool Wrap { get; set; } = true;
}

public sealed class DirectBindingConfig
{
    public TriggerInput Trigger { get; set; } = TriggerInput.None;
    public GammaLevel Level { get; set; }
}

/// <summary>Root persisted configuration (written to %APPDATA%\GammaHotkey\config.json).</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<PresetConfig> Presets { get; set; } = new();
    public TriggerMode Mode { get; set; } = TriggerMode.Cycle;
    public CycleConfig Cycle { get; set; } = new();
    public List<DirectBindingConfig> Direct { get; set; } = new();
    public bool SwallowMouse { get; set; } = true;
    public bool ApplyToAllMonitors { get; set; } = true;
    public bool RunOnStartup { get; set; }

    /// <summary>Whether to start listening for triggers automatically on launch.</summary>
    public bool Listening { get; set; }

    public static AppConfig CreateDefault()
    {
        var cfg = new AppConfig();
        foreach (var level in GammaPresets.AllLevels)
            cfg.Presets.Add(new PresetConfig { Level = level, Value = GammaPresets.DefaultValue(level) });

        // A friendly out-of-the-box setup: cycle Normal -> Higher -> High -> Max on F13.
        cfg.Cycle.Trigger = TriggerInput.Key(KeyNames.VK_F13);
        cfg.Cycle.Steps = new List<GammaLevel>
        {
            GammaLevel.Normal,
            GammaLevel.Higher,
            GammaLevel.High,
            GammaLevel.Max,
        };

        // And a couple of direct examples (also driveable from G HUB via F14/F15).
        cfg.Direct.Add(new DirectBindingConfig { Trigger = TriggerInput.Key(KeyNames.VK_F13 + 1), Level = GammaLevel.Normal });
        cfg.Direct.Add(new DirectBindingConfig { Trigger = TriggerInput.Key(KeyNames.VK_F13 + 2), Level = GammaLevel.Max });

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
