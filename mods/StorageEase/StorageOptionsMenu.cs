using FarmMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StorageEase;

internal sealed class StorageOptionsMenu : EaseMenu
{
    protected override bool AllowMounted => true;
    private readonly ModEntry mod;
    private Rectangle panel;
    private readonly List<Rectangle> rows = new();
    private int selected;
    internal StorageOptionsMenu(ModEntry mod) : base(mod.Menu) { this.mod = mod; Layout(); }
    private void Layout()
    {
        int width = Math.Min(740, Game1.uiViewport.Width - 32);
        panel = new((Game1.uiViewport.Width - width) / 2, (Game1.uiViewport.Height - 470) / 2, width, 470);
        rows.Clear();
        for (int i = 0; i < 5; i++) rows.Add(new(panel.X + 24, panel.Y + 66 + i * 58, width - 48, 52));
    }
    private string[] Labels() => new[]
    {
        "范围：" + (mod.Config.Anywhere ? "随行模式" : "舒适模式（农场 / 农舍）"),
        "制作自动取材：" + (mod.Config.CraftFromStorage ? "开" : "关"),
        "烹饪自动取材：" + (mod.Config.CookFromStorage ? "开" : "关"),
        "长按菜单键开箱：" + (mod.Config.EnableMenuHold ? "开" : "关"),
        "返回全部箱子"
    };
    protected override void Move(Point direction) => selected = Math.Clamp(selected + (direction.Y != 0 ? direction.Y : direction.X), 0, rows.Count - 1);
    protected override void Confirm()
    {
        switch (selected)
        {
            case 0: mod.Change(c => c.Anywhere = !c.Anywhere); break;
            case 1: mod.Change(c => c.CraftFromStorage = !c.CraftFromStorage); break;
            case 2: mod.Change(c => c.CookFromStorage = !c.CookFromStorage); break;
            case 3: mod.Change(c => c.EnableMenuHold = !c.EnableMenuHold); break;
            default: Back(); break;
        }
    }
    protected override void Back()
    {
        exitThisMenu(false);
        if (mod.InRange) mod.Navigation.ShowBrowser(); else mod.Navigation.Close();
    }
    protected override void Click(int x, int y)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].Contains(x, y)) { selected = i; Confirm(); return; }
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
        Ui.Box(b, panel); Ui.Text(b, "随取随用 · 设置", panel.X + 24, panel.Y + 20);
        var labels = Labels();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i == selected) b.Draw(Game1.staminaRect, r, Ui.Accent * 0.28f);
            Ui.Text(b, labels[i], r.X + 12, r.Y + 10);
        }
        Ui.Text(b, "更改自动保存 · A / Enter 确认 · B / Esc 返回", panel.X + 24, panel.Bottom - 58, Ui.Muted);
        drawMouse(b);
    }
}
