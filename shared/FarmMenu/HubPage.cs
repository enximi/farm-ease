using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace FarmMenu;

internal sealed class HubPage : EaseMenu
{
    internal const int FirstRowId = 984210;
    private readonly GameMenu host;
    private readonly List<MenuAction> rows = new();
    private readonly List<Rectangle> bounds = new();
    private string sectionId = "";
    private int selected;
    private Rectangle panel;

    internal HubPage(MenuController menu, GameMenu host) : base(menu)
    {
        this.host = host;
        BuildRows();
        Relayout();
    }

    private void BuildRows()
    {
        rows.Clear();
        foreach (MenuSection section in Menu.Sections.Values.Where(s => s.ParentId == sectionId))
            rows.Add(new(sectionId, section.Id, () => section.Title, () => section.Description,
                () => SelectSection(section.Id), () => true, section.Order));
        rows.AddRange(Menu.Actions.Values.Where(a => a.SectionId == sectionId));
        rows.Sort((a, b) => a.Order.CompareTo(b.Order));
        if (sectionId.Length == 0)
            rows.Add(new("", "refresh", () => "刷新配置", () => "重新读取已安装功能和菜单的设置；也可按 F6。",
                Menu.ReloadAll, () => true, 1000));
        else
            rows.Add(new(sectionId, "back", () => "返回", () => "返回上一级菜单。", Back, () => true, 1000));
        selected = Math.Clamp(selected, 0, rows.Count - 1);
    }

    internal void SelectSection(string id, int row = 0)
    {
        sectionId = Menu.Sections.ContainsKey(id) ? id : "";
        selected = row;
        BuildRows();
        Relayout();
    }

    internal void RestoreSelection(HubPage previous) => SelectSection(previous.sectionId, previous.selected);

    internal void Relayout()
    {
        xPositionOnScreen = host.xPositionOnScreen;
        yPositionOnScreen = host.yPositionOnScreen;
        width = 800 + borderWidth * 2;
        height = host.height;
        int contentWidth = Math.Min(Menu.Settings.PanelWidth, width - 64);
        int contentTop = spaceToClearTopBorder + 16;
        panel = new(xPositionOnScreen + (width - contentWidth) / 2, yPositionOnScreen + contentTop,
            contentWidth, height - contentTop - borderWidth);
        int rowHeight = Math.Min(60, (panel.Height - 210) / rows.Count);
        bounds.Clear();
        for (int i = 0; i < rows.Count; i++) bounds.Add(new(panel.X, panel.Y + 58 + i * rowHeight, panel.Width, rowHeight - 4));
        populateClickableComponentList();
        host.AddTabsToClickableComponents(this);
        if (ReferenceEquals(host.GetCurrentPage(), this)) snapToDefaultClickableComponent();
    }

    internal bool IsNativeInput(SButton button)
    {
        if (button is SButton.LeftTrigger or SButton.RightTrigger) return true;
        if (button != SButton.MouseLeft) return false;
        int x = Game1.getMouseX(), y = Game1.getMouseY();
        return host.tabs.Any(tab => tab.containsPoint(x, y))
            || (host.upperRightCloseButton?.containsPoint(x, y) ?? false);
    }

    public override void populateClickableComponentList()
    {
        allClickableComponents = new List<ClickableComponent>();
        for (int i = 0; i < bounds.Count; i++)
            allClickableComponents.Add(new ClickableComponent(bounds[i], rows[i].Id)
            {
                myID = FirstRowId + i,
                upNeighborID = i == 0 ? NativeMenuIntegration.TabId : FirstRowId + i - 1,
                downNeighborID = i + 1 < bounds.Count ? FirstRowId + i + 1 : -1
            });
    }
    public override void snapToDefaultClickableComponent()
    {
        currentlySnappedComponent = getComponentWithID(FirstRowId + selected);
        if (Game1.options.SnappyMenus) snapCursorToCurrentSnappedComponent();
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Relayout();
    protected override void Move(Point direction)
    {
        int step = direction.Y != 0 ? direction.Y : direction.X;
        int next = Math.Clamp(selected + step, 0, rows.Count - 1);
        if (next == selected) return;
        selected = next;
        snapToDefaultClickableComponent();
        Game1.playSound("shiny4");
    }
    protected override void Confirm()
    {
        if (!rows[selected].Enabled()) { Game1.playSound("cancel"); return; }
        try { Game1.playSound("smallSelect"); rows[selected].Execute(); }
        catch (Exception error)
        {
            Menu.Mod.Monitor.Log(error.ToString(), LogLevel.Error);
            Menu.Notify("操作未完成，请查看 SMAPI 日志。");
        }
    }
    protected override void Back()
    {
        if (sectionId.Length == 0) { host.exitThisMenu(); return; }
        string previousId = sectionId;
        string parent = Menu.Sections[sectionId].ParentId;
        SelectSection(parent);
        selected = Math.Max(0, rows.FindIndex(row => row.Id == previousId));
        snapToDefaultClickableComponent();
        Game1.playSound("smallSelect");
    }
    protected override void Click(int x, int y)
    {
        for (int i = 0; i < bounds.Count; i++)
            if (bounds[i].Contains(x, y)) { selected = i; Confirm(); return; }
    }
    public override void draw(SpriteBatch b)
    {
        Ui.Text(b, sectionId.Length == 0 ? "农场随心" : Menu.Sections[sectionId].Title, panel.X + 12, panel.Y + 8);
        for (int i = 0; i < rows.Count; i++)
        {
            Rectangle r = bounds[i];
            if (i == selected) { b.Draw(Game1.staminaRect, r, new Color(158, 190, 118) * 0.4f); Ui.Outline(b, r, new Color(87, 119, 60), 2); }
            Ui.Text(b, (i == selected ? ">  " : "   ") + rows[i].Title(), r.X + 12,
                r.Y + Math.Max(2, (r.Height - 30) / 2), rows[i].Enabled() ? Ui.Ink : Ui.Muted * 0.65f);
        }
        Ui.Wrapped(b, rows[selected].Description(), new Rectangle(panel.X + 12, bounds[^1].Bottom + 12, panel.Width - 24, 72), Ui.Muted);
        Ui.Text(b, "方向键选择 · 可长按    A / Enter 确认    B / Esc 返回", panel.X + 12, panel.Bottom - 64, Ui.Muted);
        Ui.Text(b, "LT / RT 切换标签", panel.X + 12, panel.Bottom - 28, Ui.Muted);
    }
}
