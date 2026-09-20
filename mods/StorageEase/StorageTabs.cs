using FarmMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace StorageEase;

// Decorate the native menu rather than copying inventories into another menu.
internal sealed class StorageTabs
{
    private readonly ModEntry mod;
    private sealed class State
    {
        internal Chest? Chest;
        internal List<BoxInfo>? Catalog;
        internal List<BoxInfo> Boxes = new();
        internal readonly List<(Rectangle Bounds, int Index)> Tabs = new();
        internal Rectangle Bar, Previous, Next;
        internal int Selected;
    }
    private readonly PerScreen<State> states = new(() => new());
    internal StorageTabs(ModEntry mod) => this.mod = mod;
    internal bool IsOpen => Current() != null;
    private ItemGrabMenu? Current() => mod.InRange && Game1.activeClickableMenu is ItemGrabMenu menu
        && menu.GetType() == typeof(ItemGrabMenu) && menu.source == ItemGrabMenu.source_chest
        && menu.context is Chest chest && ReferenceEquals(menu.sourceItem, chest) && StorageCatalog.Supported(chest) ? menu : null;

    internal void Tick()
    {
        var menu = Current();
        if (menu == null) { states.Value = new(); return; }
        bool firstOpen = states.Value.Chest == null;
        Layout(menu);
        if (firstOpen && !mod.Network.Loading && !mod.Access.Busy) mod.Network.Refresh();
    }
    private void Layout(ItemGrabMenu menu)
    {
        var state = states.Value;
        var chest = (Chest)menu.context;
        if (!ReferenceEquals(state.Chest, chest) || !ReferenceEquals(state.Catalog, mod.Network.Boxes))
        {
            state.Chest = chest; state.Catalog = mod.Network.Boxes;
            state.Boxes = state.Catalog.Where(box => box.Remote || ReferenceEquals(StorageCatalog.Resolve(box), chest)).ToList();
            state.Selected = state.Boxes.FindIndex(box => ReferenceEquals(StorageCatalog.Resolve(box), chest));
            if (state.Selected < 0)
            {
                // A physically opened private/remote-disabled chest remains the
                // current tab, without granting remote access back to that chest.
                state.Boxes.Insert(0, new BoxInfo
                {
                    Name = chest.modData.TryGetValue(StorageCatalog.Prefix + "name", out var name) ? name : chest.DisplayName,
                    Place = chest.Location?.DisplayName ?? Game1.currentLocation.DisplayName,
                    X = (int)chest.TileLocation.X, Y = (int)chest.TileLocation.Y
                });
                state.Selected = 0;
            }
        }
        int width = Math.Min(Math.Max(menu.ItemsToGrabMenu.width, menu.inventory.width), Game1.uiViewport.Width - 24);
        int x = Math.Clamp(menu.ItemsToGrabMenu.xPositionOnScreen + menu.ItemsToGrabMenu.width / 2 - width / 2,
            12, Math.Max(12, Game1.uiViewport.Width - width - 12));
        // Use the empty header just above the slots, below the native color picker.
        // This does not move the inventory or reduce the space available for items.
        state.Bar = new Rectangle(x, Math.Max(4, menu.ItemsToGrabMenu.yPositionOnScreen - 44), width, 40);
        state.Previous = new(x, state.Bar.Y, 56, state.Bar.Height);
        state.Next = new(state.Bar.Right - 56, state.Bar.Y, 56, state.Bar.Height);
        int count = Math.Min(state.Boxes.Count, Math.Clamp((width - 120) / 170, 1, 4));
        int start = Math.Clamp(state.Selected - count / 2, 0, state.Boxes.Count - count);
        int tabWidth = (width - 120) / count;
        state.Tabs.Clear();
        for (int i = 0; i < count; i++)
            state.Tabs.Add((new(x + 60 + i * tabWidth, state.Bar.Y, tabWidth - 4, state.Bar.Height), start + i));
    }
    internal void OnButton(ButtonPressedEventArgs e)
    {
        var menu = Current();
        if (menu == null) return;
        Layout(menu);
        int direction = e.Button switch
        {
            SButton.LeftTrigger or SButton.PageUp => -1,
            SButton.RightTrigger or SButton.PageDown => 1,
            _ => 0
        };
        if (direction != 0)
        {
            mod.Helper.Input.Suppress(e.Button);
            Cycle(menu, direction);
            return;
        }
        int x = Game1.getMouseX(), y = Game1.getMouseY();
        if (e.Button is not (SButton.MouseLeft or SButton.MouseRight) || !states.Value.Bar.Contains(x, y)) return;
        mod.Helper.Input.Suppress(e.Button);
        if (e.Button != SButton.MouseLeft) return;
        var state = states.Value;
        if (state.Previous.Contains(x, y)) Cycle(menu, -1);
        else if (state.Next.Contains(x, y)) Cycle(menu, 1);
        else foreach (var tab in state.Tabs)
            if (tab.Bounds.Contains(x, y)) { Select(menu, tab.Index); break; }
    }
    private void Cycle(ItemGrabMenu menu, int direction)
    {
        var state = states.Value;
        Select(menu, (state.Selected + direction + state.Boxes.Count) % state.Boxes.Count);
    }
    private void Select(ItemGrabMenu menu, int index)
    {
        var state = states.Value;
        if (index == state.Selected) return;
        if (mod.Network.Loading || mod.Access.Pending) { mod.Menu.Notify("正在同步箱子，请稍候。"); return; }
        mod.Access.Switch(menu, state.Boxes[index]);
    }
    internal void Draw(SpriteBatch b)
    {
        var menu = Current();
        if (menu == null) return;
        Layout(menu);
        var state = states.Value;
        bool pending = mod.Access.Pending || mod.Network.Loading;
        DrawTab(b, state.Previous, "< LT", false, state.Boxes.Count < 2 || pending);
        DrawTab(b, state.Next, "RT >", false, state.Boxes.Count < 2 || pending);
        foreach (var (bounds, index) in state.Tabs)
        {
            var box = state.Boxes[index];
            string label = $"{index + 1}. {box.Name}";
            if (index == state.Selected && pending) label = "同步中… " + label;
            DrawTab(b, bounds, Fit(label, bounds.Width - 18), index == state.Selected, pending && index != state.Selected);
        }
        int x = Game1.getMouseX(), y = Game1.getMouseY();
        if (state.Bar.Contains(x, y))
        {
            string hint = "LT / RT · PageUp / PageDown 切换箱子";
            foreach (var (bounds, index) in state.Tabs)
                if (bounds.Contains(x, y))
                {
                    var box = state.Boxes[index];
                    hint = $"{box.Name} · {box.Place} ({box.X},{box.Y})\n第 {index + 1} / {state.Boxes.Count} 个箱子\n" + hint;
                    break;
                }
            IClickableMenu.drawHoverText(b, hint, Game1.smallFont);
            if (!Game1.options.hardwareCursor) menu.drawMouse(b);
        }
    }
    private static void DrawTab(SpriteBatch b, Rectangle bounds, string label, bool selected, bool disabled)
    {
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60),
            bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 0.5f, drawShadow: false);
        if (selected)
        {
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 6, bounds.Y + 4, bounds.Width - 12, bounds.Height - 8), Ui.Accent * 0.28f);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 6, bounds.Bottom - 4, bounds.Width - 12, 3), Ui.Accent);
        }
        // A compact label leaves the native inventory at its original scale.
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(bounds.X + 8, bounds.Y + 8), disabled ? Ui.Muted : Ui.Ink, 0.75f);
    }
    private static string Fit(string text, int width)
    {
        if (Game1.smallFont.MeasureString(text).X * 0.75f <= width) return text;
        while (text.Length > 0 && Game1.smallFont.MeasureString(text + "…").X * 0.75f > width) text = text[..^1];
        return text + "…";
    }
    internal void Reset()
    {
        if (Context.IsMainPlayer) states.ResetAllScreens(); else states.Value = new();
    }
}
