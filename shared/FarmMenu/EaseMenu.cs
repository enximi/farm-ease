using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace FarmMenu;

internal abstract class EaseMenu : IClickableMenu
{
    protected readonly MenuController Menu;
    private Point lastDirection;
    private double repeatAt;
    protected EaseMenu(MenuController menu) : base(0, 0, 0, 0) => Menu = menu;
    public override bool areGamePadControlsImplemented() => true;
    public override void receiveKeyPress(Keys key) { }
    public override void receiveGamePadButton(Buttons button) { }
    public override void receiveLeftClick(int x, int y, bool playSound = true) { }
    public override void receiveRightClick(int x, int y, bool playSound = true) { }

    internal void HandleButton(SButton button)
    {
        if (this is not HubPage && !Menu.CanUse(out _, true)) return;
        Point direction = Direction(button);
        if (direction != Point.Zero)
        {
            Move(direction);
            lastDirection = direction;
            repeatAt = Now + Menu.Settings.RepeatDelayMilliseconds;
        }
        else if (button is SButton.ControllerA or SButton.Enter or SButton.Space) Confirm();
        else if (button is SButton.ControllerB or SButton.Escape or SButton.MouseRight) Back();
        else if (button is SButton.ControllerX or SButton.Z) Undo();
        else if (button == SButton.MouseLeft) Click(Game1.getMouseX(), Game1.getMouseY());
        else if (button == SButton.F6) Menu.Hub?.Refresh();
    }

    internal virtual void TickInput()
    {
        if (this is not HubPage && !Menu.CanUse(out _, true)) { exitThisMenu(false); return; }
        Point direction = Point.Zero;
        foreach (SButton key in DirectionButtons)
        {
            // Suppression persists until physical release; IsDown alone becomes false on the next tick.
            if (!Menu.Mod.Helper.Input.IsDown(key) && !Menu.Mod.Helper.Input.IsSuppressed(key)) continue;
            Menu.Mod.Helper.Input.Suppress(key);
            if (direction == Point.Zero) direction = Direction(key);
        }
        if (direction == Point.Zero) { lastDirection = Point.Zero; return; }
        if (direction != lastDirection) { lastDirection = direction; repeatAt = Now + Menu.Settings.RepeatDelayMilliseconds; }
        else if (Now >= repeatAt) { Move(direction); repeatAt = Now + Menu.Settings.RepeatIntervalMilliseconds; }
    }

    private double Now => Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
    private static readonly SButton[] DirectionButtons = {
        SButton.DPadUp, SButton.DPadDown, SButton.DPadLeft, SButton.DPadRight,
        SButton.LeftThumbstickUp, SButton.LeftThumbstickDown, SButton.LeftThumbstickLeft, SButton.LeftThumbstickRight,
        SButton.Up, SButton.Down, SButton.Left, SButton.Right, SButton.W, SButton.S, SButton.A, SButton.D
    };
    private static Point Direction(SButton b) => b switch
    {
        SButton.DPadUp or SButton.LeftThumbstickUp or SButton.Up or SButton.W => new(0, -1),
        SButton.DPadDown or SButton.LeftThumbstickDown or SButton.Down or SButton.S => new(0, 1),
        SButton.DPadLeft or SButton.LeftThumbstickLeft or SButton.Left or SButton.A => new(-1, 0),
        SButton.DPadRight or SButton.LeftThumbstickRight or SButton.Right or SButton.D => new(1, 0), _ => Point.Zero
    };
    protected abstract void Move(Point direction);
    protected abstract void Confirm();
    protected abstract void Back();
    protected virtual void Undo() { }
    protected abstract void Click(int x, int y);
}
