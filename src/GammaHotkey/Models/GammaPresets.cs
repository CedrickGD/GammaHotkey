namespace GammaHotkey.Models;

/// <summary>The six named gamma levels the user asked for.</summary>
public enum GammaLevel
{
    Low,
    Mid,
    Normal,
    Higher,
    High,
    Max,
}

/// <summary>Range + default values for the gamma presets.</summary>
public static class GammaPresets
{
    /// <summary>Lowest selectable gamma. Not 0.0 exactly (a black screen) – the slider
    /// floor. The engine clamps anything below this.</summary>
    public const double Min = 0.10;

    /// <summary>Highest selectable gamma (user spec: up to 2.5).</summary>
    public const double Max = 2.50;

    /// <summary>Neutral / Windows default. Produces an identity ramp.</summary>
    public const double Default = 1.00;

    /// <summary>Slider / stepper granularity.</summary>
    public const double Step = 0.05;

    public static readonly IReadOnlyList<GammaLevel> AllLevels = new[]
    {
        GammaLevel.Low,
        GammaLevel.Mid,
        GammaLevel.Normal,
        GammaLevel.Higher,
        GammaLevel.High,
        GammaLevel.Max,
    };

    /// <summary>Sensible starting values spanning the full range. Higher gamma is
    /// brighter mid-tones, so the list climbs Low -> Max.</summary>
    public static double DefaultValue(GammaLevel level) => level switch
    {
        GammaLevel.Low => 0.50,
        GammaLevel.Mid => 0.75,
        GammaLevel.Normal => 1.00,
        GammaLevel.Higher => 1.40,
        GammaLevel.High => 1.90,
        GammaLevel.Max => 2.50,
        _ => 1.00,
    };

    public static string DisplayName(GammaLevel level) => level switch
    {
        GammaLevel.Low => "Low",
        GammaLevel.Mid => "Mid",
        GammaLevel.Normal => "Normal",
        GammaLevel.Higher => "Higher",
        GammaLevel.High => "High",
        GammaLevel.Max => "Max",
        _ => level.ToString(),
    };

    public static double Clamp(double value) => Math.Clamp(value, Min, Max);
}
