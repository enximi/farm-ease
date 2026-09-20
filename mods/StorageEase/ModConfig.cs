namespace StorageEase;

public sealed class ModConfig
{
    public bool Anywhere { get; set; } = true;
    public bool CraftFromStorage { get; set; } = true;
    public bool CookFromStorage { get; set; }
    internal ModConfig Copy() => (ModConfig)MemberwiseClone();
}
