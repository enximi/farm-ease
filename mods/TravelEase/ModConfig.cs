using StardewModdingAPI;
namespace TravelEase;

public sealed class ModConfig
{
    public SButton HomeButton { get; set; } = SButton.RightStick;
    public SButton KeyboardHomeButton { get; set; } = SButton.F8;
    public int HomeHoldMilliseconds { get; set; } = 700;
    public bool EnableSleepShortcut { get; set; } = true;
    public int SleepHoldMilliseconds { get; set; } = 1200;
    internal void Normalize()
    {
        HomeHoldMilliseconds = Math.Clamp(HomeHoldMilliseconds, 350, 2500);
        SleepHoldMilliseconds = Math.Clamp(SleepHoldMilliseconds, 1000, 3000);
        if (HomeButton == SButton.None) HomeButton = SButton.RightStick;
        if (KeyboardHomeButton == SButton.None) KeyboardHomeButton = SButton.F8;
    }
}
