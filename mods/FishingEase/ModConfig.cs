namespace FishingEase;

internal sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public float BarSizeMultiplier { get; set; } = 1.2f;
    public int GracePeriodMilliseconds { get; set; } = 300;
    public int GraceResetMilliseconds { get; set; } = 500;
    public float ProgressLossMultiplier { get; set; } = 0.6f;
    public float BottomBounceMultiplier { get; set; } = 0.25f;

    public ModConfig Snapshot() => (ModConfig)MemberwiseClone();

    public bool Normalize()
    {
        ModConfig before = Snapshot();
        BarSizeMultiplier = float.IsFinite(BarSizeMultiplier) ? Math.Clamp(BarSizeMultiplier, 1f, 1.5f) : 1.2f;
        GracePeriodMilliseconds = Math.Clamp(GracePeriodMilliseconds, 0, 1000);
        GraceResetMilliseconds = Math.Clamp(GraceResetMilliseconds, 100, 2000);
        ProgressLossMultiplier = float.IsFinite(ProgressLossMultiplier) ? Math.Clamp(ProgressLossMultiplier, 0.1f, 1f) : 0.6f;
        BottomBounceMultiplier = float.IsFinite(BottomBounceMultiplier) ? Math.Clamp(BottomBounceMultiplier, 0f, 1f) : 0.25f;
        return before.BarSizeMultiplier != BarSizeMultiplier
            || before.GracePeriodMilliseconds != GracePeriodMilliseconds
            || before.GraceResetMilliseconds != GraceResetMilliseconds
            || before.ProgressLossMultiplier != ProgressLossMultiplier
            || before.BottomBounceMultiplier != BottomBounceMultiplier;
    }

    public static ModConfig? Preset(string name) => name.ToLowerInvariant() switch
    {
        "light" => new() { BarSizeMultiplier = 1.1f, GracePeriodMilliseconds = 150, ProgressLossMultiplier = 0.8f, BottomBounceMultiplier = 0.5f },
        "normal" => new(),
        "strong" => new() { BarSizeMultiplier = 1.35f, GracePeriodMilliseconds = 500, ProgressLossMultiplier = 0.35f, BottomBounceMultiplier = 0.1f },
        "off" => new() { Enabled = false },
        _ => null
    };
}
