using FarmMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace StorageEase;

internal sealed class StorageBrowser : IClickableMenu
{
    private readonly ModEntry mod;
    private readonly TextBox search;
    private Rectangle panel;
    private readonly List<Rectangle> rows = new();
    private readonly List<Rectangle> actions = new();
    private List<BoxInfo> filtered = new();
    private int selected, first, action;
    private string lastQuery = "";
    private long refreshed;
    private static readonly string[] Labels = { "打开", "远程开关", "取材开关", "共享开关", "改名", "刷新" };
    internal StorageBrowser(ModEntry mod)
    {
        this.mod = mod;
        search = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
        { Text = "", Width = 400, Selected = false };
        search.OnEnterPressed += _ => search.Selected = false;
        Layout(); UpdateRows();
    }
    private void Layout()
    {
        int w = Math.Min(1060, Game1.uiViewport.Width - 32), h = Math.Min(760, Game1.uiViewport.Height - 32);
        panel = new((Game1.uiViewport.Width - w) / 2, (Game1.uiViewport.Height - h) / 2, w, h);
        xPositionOnScreen = panel.X; yPositionOnScreen = panel.Y; width = w; height = h;
        initializeUpperRightCloseButton();
        search.X = panel.X + 24; search.Y = panel.Y + 62; search.Width = Math.Max(240, w - 200);
        rows.Clear();
        for (int i = 0; i < Math.Max(1, (h - 280) / 62); i++) rows.Add(new(panel.X + 24, panel.Y + 124 + i * 62, w - 48, 58));
        actions.Clear();
        int actionWidth = (w - 48) / Labels.Length;
        for (int i = 0; i < Labels.Length; i++) actions.Add(new(panel.X + 24 + i * actionWidth, panel.Bottom - 124, actionWidth - 6, 42));
        KeepVisible();
    }
    private void UpdateRows()
    {
        string query = search.Text.Trim();
        string? id = selected < filtered.Count ? filtered[selected].Id : null;
        filtered = mod.Network.Boxes.Where(info => query.Length == 0 || Contains(info.Name, query) || Contains(info.Place, query)
            || (StorageCatalog.Resolve(info)?.GetItemsForPlayer().Any(item => item != null && Contains(item.DisplayName, query)) == true)).ToList();
        int index = filtered.FindIndex(b => b.Id == id);
        selected = index >= 0 ? index : Math.Clamp(selected, 0, Math.Max(0, filtered.Count - 1));
        lastQuery = search.Text; refreshed = Environment.TickCount64; KeepVisible();
    }
    private static bool Contains(string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
    private void KeepVisible()
    {
        if (selected < first) first = selected;
        if (selected >= first + rows.Count) first = selected - rows.Count + 1;
        first = Math.Clamp(first, 0, Math.Max(0, filtered.Count - rows.Count));
    }
    public override void update(GameTime time)
    {
        base.update(time);
        if (!mod.InRange) { exitThisMenu(); return; }
        if (search.Text != lastQuery || Environment.TickCount64 - refreshed > 500) UpdateRows();
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();
    public override bool areGamePadControlsImplemented() => true;
    public override void receiveKeyPress(Keys key)
    {
        if (search.Selected)
        {
            if (key is Keys.Escape or Keys.Enter) search.Selected = false;
            return;
        }
        switch (key)
        {
            case Keys.Escape: Back(); break;
            case Keys.Up: Move(-1); break;
            case Keys.Down: Move(1); break;
            case Keys.PageUp: Move(-rows.Count); break;
            case Keys.PageDown: Move(rows.Count); break;
            case Keys.Left: action = (action + Labels.Length - 1) % Labels.Length; break;
            case Keys.Right: case Keys.Tab: action = (action + 1) % Labels.Length; break;
            case Keys.Enter: Activate(); break;
            case Keys.F: search.Selected = true; Game1.keyboardDispatcher.Subscriber = search; break;
        }
    }
    public override void receiveGamePadButton(Buttons button)
    {
        switch (button)
        {
            case Buttons.A: Activate(); break;
            case Buttons.B: Back(); break;
            case Buttons.DPadUp: case Buttons.LeftThumbstickUp: Move(-1); break;
            case Buttons.DPadDown: case Buttons.LeftThumbstickDown: Move(1); break;
            case Buttons.DPadLeft: case Buttons.LeftThumbstickLeft: action = (action + Labels.Length - 1) % Labels.Length; break;
            case Buttons.DPadRight: case Buttons.LeftThumbstickRight: action = (action + 1) % Labels.Length; break;
            case Buttons.LeftShoulder: Move(-rows.Count); break;
            case Buttons.RightShoulder: Move(rows.Count); break;
            case Buttons.Y: search.Selected = true; Game1.showTextEntry(search); break;
        }
    }
    private void Move(int amount) { selected = Math.Clamp(selected + amount, 0, Math.Max(0, filtered.Count - 1)); KeepVisible(); }
    public override void receiveScrollWheelAction(int direction) { if (!search.Selected) Move(direction > 0 ? -1 : 1); }
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (upperRightCloseButton?.containsPoint(x, y) == true) { Back(); return; }
        search.Selected = new Rectangle(search.X, search.Y, search.Width, 48).Contains(x, y);
        if (search.Selected) { Game1.keyboardDispatcher.Subscriber = search; return; }
        for (int i = 0; i < rows.Count; i++) if (rows[i].Contains(x, y) && first + i < filtered.Count) { selected = first + i; return; }
        for (int i = 0; i < actions.Count; i++) if (actions[i].Contains(x, y)) { action = i; Activate(); return; }
    }
    public override void receiveRightClick(int x, int y, bool playSound = true) => Back();
    private void Activate()
    {
        if (mod.Network.Loading || mod.Access.Busy) { mod.Menu.Notify("正在同步仓储，请稍候。"); return; }
        if (action == 5) { mod.Network.Refresh(); return; }
        if (filtered.Count == 0) return;
        BoxInfo info = filtered[selected];
        if (action == 0) { search.Selected = false; mod.Access.Open(info); return; }
        if (info.Owner != Game1.player.UniqueMultiplayerID) { mod.Menu.Notify("共享箱子的设置由箱子主人修改。"); return; }
        switch (action)
        {
            case 1: mod.Network.Refresh(info.Id, "remote", (!info.Remote).ToString().ToLowerInvariant()); break;
            case 2: mod.Network.Refresh(info.Id, "craft", (!info.Craft).ToString().ToLowerInvariant()); break;
            case 3: mod.Network.Refresh(info.Id, "shared", (!info.Shared).ToString().ToLowerInvariant()); break;
            case 4:
                search.Selected = false;
                Game1.activeClickableMenu = new NamingMenu(name =>
                {
                    Game1.activeClickableMenu = new StorageBrowser(mod);
                    mod.Network.Refresh(info.Id, "name", name.Trim());
                }, "箱子名称（最多 32 字）", info.Name);
                break;
        }
    }
    private void Back()
    {
        if (mod.Access.Busy) mod.Access.Release();
        exitThisMenu(false); mod.Menu.Hub?.Open("storage");
    }
    protected override void cleanupBeforeExit()
    {
        search.Selected = false;
        if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, search)) Game1.keyboardDispatcher.Subscriber = null;
        base.cleanupBeforeExit();
    }
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.45f);
        Ui.Box(b, panel);
        Ui.Text(b, "随取随用 · " + (mod.Config.Anywhere ? "随行模式" : "舒适模式"), panel.X + 24, panel.Y + 18);
        search.Draw(b);
        if (search.Text.Length == 0 && !search.Selected) Ui.Text(b, "搜索箱名、地点或箱内物品", search.X + 12, search.Y + 10, Ui.Muted);
        Ui.Text(b, $"{filtered.Count} 个箱子", search.X + search.Width + 16, search.Y + 10, Ui.Muted);
        for (int i = 0; i < rows.Count && first + i < filtered.Count; i++)
        {
            BoxInfo info = filtered[first + i]; Rectangle r = rows[i];
            if (first + i == selected) { b.Draw(Game1.staminaRect, r, Ui.Accent * 0.28f); Ui.Outline(b, r, Ui.Accent, 2); }
            string title = $"{info.Name} · {info.Place} ({info.X},{info.Y})";
            while (title.Length > 0 && Game1.smallFont.MeasureString(title).X > r.Width - 24) title = title[..^1];
            Ui.Text(b, title, r.X + 10, r.Y + 1);
            string flags = $"远程 {(info.Remote ? "开" : "关")}    取材 {(info.Craft ? "开" : "关")}    {(info.Shared ? "已共享" : "私人")}";
            var chest = StorageCatalog.Resolve(info);
            if (chest == null) flags += "    等待同步";
            else if (chest.GetMutex().IsLocked()) flags += "    使用中";
            Ui.Text(b, flags, r.X + 10, r.Y + 29, Ui.Muted);
        }
        if (filtered.Count == 0) Ui.Wrapped(b, mod.Network.Loading ? "正在同步箱子……" : "没有匹配的箱子。自己的普通箱子会自动列入；其他玩家的箱子需要主人开启共享。", new(panel.X + 30, panel.Y + 135, panel.Width - 60, 90), Ui.Muted);
        for (int i = 0; i < actions.Count; i++)
        {
            Rectangle r = actions[i];
            if (i == action) b.Draw(Game1.staminaRect, r, Ui.Accent * 0.4f);
            Ui.Outline(b, r, Ui.Muted, 1); Ui.Text(b, Labels[i], r.X + 6, r.Y + 6);
        }
        string status = mod.Network.Loading || mod.Access.Busy ? "正在同步或等待箱子……" : mod.Network.Message;
        Ui.Text(b, status, panel.X + 24, panel.Bottom - 77, Ui.Muted);
        Ui.Text(b, "↑↓ 选箱 · ←→ 选操作 · A / Enter 确认 · B / Esc 返回 · F / Y 搜索", panel.X + 24, panel.Bottom - 43, Ui.Muted);
        upperRightCloseButton?.draw(b); drawMouse(b);
    }
}
