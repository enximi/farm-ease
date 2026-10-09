using FarmMenu;
using StardewModdingAPI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace TimeEase;

internal sealed class RestMenu : TimeMenu
{
    private readonly Func<string> message;
    private readonly Action returnToTitle;
    private readonly Action settings;
    private readonly Action extend;
    private readonly string primaryLabel;
    private int selected;
    protected override int FocusId => selected;
    private Rectangle Panel => new((Game1.uiViewport.Width - Math.Min(700, Game1.uiViewport.Width - 32)) / 2,
        (Game1.uiViewport.Height - Math.Min(460, Game1.uiViewport.Height - 24)) / 2,
        Math.Min(700, Game1.uiViewport.Width - 32), Math.Min(460, Game1.uiViewport.Height - 24));
    private Rectangle Button(int i) => new(Panel.X + 24, Panel.Bottom - 174 + i * 48, Panel.Width - 48, 42);
    internal RestMenu(ModEntry mod, Func<string> message, Action returnToTitle, Action settings, Action extend, string primaryLabel = "返回标题") : base(mod)
    { this.message = message; this.returnToTitle = returnToTitle; this.settings = settings; this.extend = extend; this.primaryLabel = primaryLabel; Relayout(); }
    public override void populateClickableComponentList()
    {
        xPositionOnScreen = Panel.X; yPositionOnScreen = Panel.Y; width = Panel.Width; height = Panel.Height;
        allClickableComponents = new();
        for (int i = 0; i < 3; i++)
            allClickableComponents.Add(new ClickableComponent(Button(i), "time.rest." + i)
            { myID = i, upNeighborID = i > 0 ? i - 1 : -1, downNeighborID = i < 2 ? i + 1 : -1,
                leftNeighborID = i > 0 ? i - 1 : -1, rightNeighborID = i < 2 ? i + 1 : -1, fullyImmutable = true });
    }
    protected override void Move(Point direction, bool fast)
    { selected = Math.Clamp(selected + (direction.Y != 0 ? direction.Y : direction.X), 0, 2); Focus(); }
    protected override void Controller(SButton button)
    {
        if (button == SButton.ControllerX) settings();
        else if (button == SButton.ControllerY) extend();
        else if (button == SButton.ControllerA) Activate();
        else if (button is SButton.ControllerB or SButton.ControllerBack or SButton.ControllerStart)
        { if (primaryLabel != "每日设置") ReturnToTitle(); }
    }
    private void Activate()
    { if (selected == 1) settings(); else if (selected == 2) extend(); else ReturnToTitle(); }
    public override bool readyToClose() => false;
    public override void receiveKeyPress(Keys key)
    {
        if (MoveKey(key)) return;
        if (key == Keys.F11) settings();
        else if (key == Keys.F12) extend();
        else if (key is Keys.Enter or Keys.Space) Activate();
        else if (key == Keys.Escape && primaryLabel != "每日设置") ReturnToTitle();
    }
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        for (int i = 0; i < 3; i++)
            if (Button(i).Contains(x, y)) { selected = i; Focus(); Activate(); return; }
    }
    // Only an explicit player action exits. This menu is only opened on load or
    // after proven persistence and native settlement, never mid-day at the limit.
    private void ReturnToTitle() => returnToTitle();
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.8f);
        Ui.Box(b, Panel);
        Ui.Text(b, "适时而归 · 休息时间", Panel.X + 24, Panel.Y + 18);
        Ui.Wrapped(b, message(), new Rectangle(Panel.X + 24, Panel.Y + 64, Panel.Width - 48, Math.Max(40, Panel.Height - 242)));
        string[] labels = { primaryLabel, "设置 · X", "今日加时 · Y" };
        for (int i = 0; i < 3; i++)
        { Ui.Box(b, Button(i)); if (i == selected) Ui.Outline(b, Button(i), Ui.Accent); Ui.Text(b, labels[i], Button(i).X + 16, Button(i).Y + 8); }
        Ui.Text(b, primaryLabel == "每日设置" ? "↑↓选择 · A 确认 · X 设置 · Y 加时" : "↑↓选择 · A 确认 · B 返回标题", Panel.X + 24, Panel.Bottom - 28, Ui.Muted);
        drawMouse(b);
    }
}
