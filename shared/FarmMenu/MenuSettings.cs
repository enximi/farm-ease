using System.Text.Json;
using System.Text.Json.Serialization;
using StardewModdingAPI;

namespace FarmMenu;

internal sealed class MenuSettings
{
    public SButton MenuButton { get; set; } = SButton.LeftStick;
    public SButton KeyboardMenuButton { get; set; } = SButton.F7;
    public int RepeatDelayMilliseconds { get; set; } = 250;
    public int RepeatIntervalMilliseconds { get; set; } = 70;
    public int PanelWidth { get; set; } = 700;

    internal void Normalize()
    {
        if (MenuButton == SButton.None) MenuButton = SButton.LeftStick;
        if (KeyboardMenuButton == SButton.None) KeyboardMenuButton = SButton.F7;
        RepeatDelayMilliseconds = Math.Clamp(RepeatDelayMilliseconds, 150, 800);
        RepeatIntervalMilliseconds = Math.Clamp(RepeatIntervalMilliseconds, 60, 300);
        PanelWidth = Math.Clamp(PanelWidth, 540, 1000);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
