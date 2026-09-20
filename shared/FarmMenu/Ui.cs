using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace FarmMenu;

internal static class Ui
{
    internal static readonly Color Accent = new(117, 207, 150);
    internal static readonly Color Ink = new(60, 44, 36);
    internal static readonly Color Muted = new(114, 91, 72);
    internal static void Box(SpriteBatch b, Rectangle r) => IClickableMenu.drawTextureBox(b, r.X, r.Y, r.Width, r.Height, Color.White);
    internal static void Text(SpriteBatch b, string text, int x, int y, Color? color = null)
        => Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(x, y), color ?? Ink);
    internal static void Wrapped(SpriteBatch b, string text, Rectangle r, Color? color = null)
        => Text(b, Game1.parseText(text, Game1.smallFont, r.Width), r.X, r.Y, color);
    internal static void Outline(SpriteBatch b, Rectangle r, Color color, int thickness = 3)
    {
        b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
        b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, thickness, r.Height), color);
        b.Draw(Game1.staminaRect, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
    }
    internal static string Button(SButton button) => button switch
    {
        SButton.LeftStick => "L3（左摇杆按下）", SButton.RightStick => "R3（右摇杆按下）",
        SButton.LeftShoulder => "LB", SButton.RightShoulder => "RB",
        SButton.LeftTrigger => "LT", SButton.RightTrigger => "RT",
        SButton.ControllerA => "A", SButton.ControllerB => "B", SButton.ControllerX => "X", SButton.ControllerY => "Y",
        SButton.ControllerBack => "View", SButton.ControllerStart => "Menu", _ => button.ToString()
    };
}
