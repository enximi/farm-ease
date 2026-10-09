using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace TimeEase;

// Use the same SMAPI suppression and hold-repeat pattern as FarmMenu.EaseMenu.
// Native button-to-key mapping must not confirm or cancel a second time.
internal abstract class TimeMenu : IClickableMenu
{
    private readonly ModEntry mod;
    private readonly int screen = Context.ScreenId;
    private SButton held;
    private long repeatAt;
    protected TimeMenu(ModEntry mod) => this.mod = mod;
    public override bool areGamePadControlsImplemented() => true;
    public override void receiveGamePadButton(Buttons button) { }
    protected abstract int FocusId { get; }
    protected abstract void Move(Point direction, bool fast);
    protected abstract void Controller(SButton button);

    internal bool HandleController(SButton button)
    {
        if (Context.ScreenId != screen) return false;
        Point direction = Direction(button);
        if (direction != Point.Zero)
        {
            Move(direction, Fast(button));
            held = button;
            repeatAt = Environment.TickCount64 + 300;
            return true;
        }
        if (button is SButton.ControllerA or SButton.ControllerB or SButton.ControllerX or SButton.ControllerY
            or SButton.ControllerBack or SButton.ControllerStart or SButton.LeftStick or SButton.RightStick
            or SButton.LeftTrigger or SButton.RightTrigger)
        { mod.NetworkLog("controller_action", $"menu={GetType().Name} button={button} focus={FocusId} screen={screen}"); Controller(button); return true; }
        return false;
    }

    internal void TickController()
    {
        if (Context.ScreenId != screen) return;
        SButton down = SButton.None;
        foreach (var button in MovementButtons)
        {
            if (!mod.Helper.Input.IsDown(button) && !mod.Helper.Input.IsSuppressed(button)) continue;
            mod.Helper.Input.Suppress(button);
            if (down == SButton.None) down = button;
        }
        if (down == SButton.None) { held = SButton.None; return; }
        if (down != held) { held = down; repeatAt = Environment.TickCount64 + 300; return; }
        if (Environment.TickCount64 >= repeatAt)
        { Move(Direction(down), Fast(down)); repeatAt = Environment.TickCount64 + 90; }
    }

    protected void Focus()
    {
        currentlySnappedComponent = getComponentWithID(FocusId);
        if (Game1.options.SnappyMenus && currentlySnappedComponent != null) snapCursorToCurrentSnappedComponent();
    }
    protected void Relayout()
    { populateClickableComponentList(); Focus(); }
    public override void snapToDefaultClickableComponent() => Focus();
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Relayout();
    public override void applyMovementKey(int direction) => Move(direction switch
    { 0 => new(0, -1), 1 => new(1, 0), 2 => new(0, 1), 3 => new(-1, 0), _ => Point.Zero }, false);
    protected bool MoveKey(Keys key)
    {
        if (key == Keys.Up || Game1.options.doesInputListContain(Game1.options.moveUpButton, key)) Move(new(0, -1), false);
        else if (key == Keys.Down || Game1.options.doesInputListContain(Game1.options.moveDownButton, key)) Move(new(0, 1), false);
        else if (key == Keys.Left || Game1.options.doesInputListContain(Game1.options.moveLeftButton, key)) Move(new(-1, 0), false);
        else if (key == Keys.Right || Game1.options.doesInputListContain(Game1.options.moveRightButton, key)) Move(new(1, 0), false);
        else return false;
        return true;
    }
    private static bool Fast(SButton button) => button is SButton.LeftShoulder or SButton.RightShoulder;
    private static Point Direction(SButton button) => button switch
    {
        SButton.DPadUp or SButton.LeftThumbstickUp => new(0, -1),
        SButton.DPadDown or SButton.LeftThumbstickDown => new(0, 1),
        SButton.DPadLeft or SButton.LeftThumbstickLeft or SButton.LeftShoulder => new(-1, 0),
        SButton.DPadRight or SButton.LeftThumbstickRight or SButton.RightShoulder => new(1, 0), _ => Point.Zero
    };
    private static readonly SButton[] MovementButtons = {
        SButton.DPadUp, SButton.DPadDown, SButton.DPadLeft, SButton.DPadRight,
        SButton.LeftThumbstickUp, SButton.LeftThumbstickDown, SButton.LeftThumbstickLeft, SButton.LeftThumbstickRight,
        SButton.LeftShoulder, SButton.RightShoulder
    };
}
