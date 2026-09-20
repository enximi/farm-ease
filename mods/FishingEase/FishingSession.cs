using Microsoft.Xna.Framework;
using StardewValley.Menus;

namespace FishingEase;

/// <summary>One immutable configuration snapshot and grace budget per hooked fish.</summary>
internal sealed class FishingSession
{
    public ModConfig Config { get; }
    public double GraceRemainingMilliseconds { get; private set; }
    public float LossMultiplier { get; private set; } = 1f;
    private double stableMilliseconds;

    public FishingSession(ModConfig config)
    {
        Config = config.Snapshot();
        GraceRemainingMilliseconds = Config.GracePeriodMilliseconds;
    }

    public void ResizeBar(BobberBar menu)
    {
        if (!Config.Enabled || Config.BarSizeMultiplier == 1f)
            return;

        int oldHeight = menu.bobberBarHeight;
        // Preserve the bottom edge at hook time, including the original tackle/food bonuses.
        // Never shorten a bar made larger by another mod.
        menu.bobberBarHeight = Math.Max(oldHeight,
            Math.Min(BobberBar.bobberBarTrackHeight - 16, (int)Math.Round(oldHeight * Config.BarSizeMultiplier)));
        menu.bobberBarPos = Math.Max(0, menu.bobberBarPos - (menu.bobberBarHeight - oldHeight));
    }

    public void Update(BobberBar menu, GameTime time)
    {
        if (!Config.Enabled)
        {
            LossMultiplier = 1f;
            return;
        }

        double elapsed = Math.Max(0, time.ElapsedGameTime.TotalMilliseconds);
        LossMultiplier = Config.ProgressLossMultiplier;
        if (menu.bobberInBar)
        {
            stableMilliseconds += elapsed;
            if (stableMilliseconds >= Config.GraceResetMilliseconds)
                GraceRemainingMilliseconds = Config.GracePeriodMilliseconds;
            return;
        }

        stableMilliseconds = 0;
        // Brief re-entry does not refill the budget; only sustained tracking does.
        double protectedTime = Math.Min(GraceRemainingMilliseconds, elapsed);
        GraceRemainingMilliseconds -= protectedTime;
        LossMultiplier *= elapsed > 0
            ? (float)((elapsed - protectedTime) / elapsed)
            : (GraceRemainingMilliseconds > 0 ? 0f : 1f);
    }
}
