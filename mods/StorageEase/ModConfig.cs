using StardewModdingAPI;

namespace StorageEase;

public sealed class ModConfig
{
    public bool Anywhere { get; set; } = true;
    public bool CraftFromStorage { get; set; } = true;
    public bool CookFromStorage { get; set; }
    public bool EnableMenuHold { get; set; } = true;
    public int MenuHoldMilliseconds { get; set; } = 500;
    public SButton KeyboardOpenButton { get; set; } = SButton.F9;
    internal void Normalize() => MenuHoldMilliseconds = Math.Clamp(MenuHoldMilliseconds, 250, 2000);
    internal ModConfig Copy() => (ModConfig)MemberwiseClone();
}
