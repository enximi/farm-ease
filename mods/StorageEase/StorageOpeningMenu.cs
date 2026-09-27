using FarmMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StorageEase;

// A cancellable waiting screen avoids opening an obsolete cached chest before
// the host has refreshed permissions and positions.
internal sealed class StorageOpeningMenu : EaseMenu
{
    private readonly ModEntry mod;
    private bool requested;
    internal StorageOpeningMenu(ModEntry mod) : base(mod.Menu) => this.mod = mod;
    internal override void TickInput()
    {
        base.TickInput();
        if (!ReferenceEquals(Game1.activeClickableMenu, this)) return;
        if (!mod.InRange) { mod.Navigation.Close(); return; }
        if (mod.Network.Loading || mod.Access.Pending) return;
        if (!requested && mod.Navigation.LastBox() is { } box)
        {
            requested = true;
            mod.Access.Open(box);
        }
        else if (!mod.Access.Busy) mod.Navigation.ShowBrowser();
    }
    protected override void Move(Point direction) { }
    protected override void Confirm() { }
    protected override void Back() => mod.Navigation.Close();
    protected override void Click(int x, int y) { }
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
        var panel = new Rectangle((Game1.uiViewport.Width - 520) / 2, (Game1.uiViewport.Height - 160) / 2, 520, 160);
        Ui.Box(b, panel);
        Ui.Text(b, "随取随用 · 正在打开上次的箱子…", panel.X + 24, panel.Y + 32);
        Ui.Text(b, "B / Esc 取消", panel.X + 24, panel.Y + 94, Ui.Muted);
        drawMouse(b);
    }
}
