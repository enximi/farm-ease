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
        internal Rectangle Bar, Previous, Next, All;
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
                // A physically opened remote-disabled chest remains the
                // current tab, without granting remote access back to that chest.
                var preview = StoragePreview.From(chest);
                state.Boxes.Insert(0, new BoxInfo
                {
                    Name = preview.Name, IconId = preview.IconId,
                    Place = chest.Location?.DisplayName ?? Game1.currentLocation.DisplayName,
                    X = (int)chest.TileLocation.X, Y = (int)chest.TileLocation.Y
                });
                state.Selected = 0;
            }
        }
        int available = Math.Max(1, Math.Min(Math.Max(menu.ItemsToGrabMenu.width, menu.inventory.width), Game1.uiViewport.Width - 24));
        int arrowWidth = Math.Min(52, available / 8), allWidth = Math.Min(112, available / 4);
        int room = Math.Max(1, available - 2 * arrowWidth - allWidth - 16);
        int pitch = Math.Min(68, room);
        int count = Math.Min(state.Boxes.Count, Math.Max(1, room / pitch));
        int width = 2 * arrowWidth + allWidth + 16 + count * pitch;
        int x = Math.Clamp(menu.ItemsToGrabMenu.xPositionOnScreen + menu.ItemsToGrabMenu.width / 2 - width / 2,
            12, Math.Max(12, Game1.uiViewport.Width - width - 12));
        // Fixed compact icon tabs: fewer boxes never stretch into wide labels.
        // Use the existing header without shifting either inventory.
        state.Bar = new Rectangle(x, Math.Max(4, menu.ItemsToGrabMenu.yPositionOnScreen - 44), width, 40);
        state.Previous = new(x, state.Bar.Y, arrowWidth, state.Bar.Height);
        state.Next = new(state.Bar.Right - arrowWidth, state.Bar.Y, arrowWidth, state.Bar.Height);
        state.All = new(state.Next.X - allWidth - 4, state.Bar.Y, allWidth, state.Bar.Height);
        int start = Math.Clamp(state.Selected - count / 2, 0, state.Boxes.Count - count);
        state.Tabs.Clear();
        for (int i = 0; i < count; i++)
            state.Tabs.Add((new(state.Previous.Right + 4 + i * pitch, state.Bar.Y, Math.Max(1, pitch - 4), state.Bar.Height), start + i));
    }
    private StoragePreview Preview(int index) => index == states.Value.Selected && states.Value.Chest is { } chest
        ? StoragePreview.From(chest) : StoragePreview.From(states.Value.Boxes[index]);

    internal void OnButton(ButtonPressedEventArgs e)
    {
        var menu = Current();
        if (menu == null) return;
        Layout(menu);
        if (e.Button == mod.Menu.Settings.MenuButton || e.Button == mod.Menu.Settings.KeyboardMenuButton)
        {
            mod.Helper.Input.Suppress(e.Button);
            mod.Navigation.ShowBrowser();
            return;
        }
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
        else if (state.All.Contains(x, y)) mod.Navigation.ShowBrowser();
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
        DrawTab(b, state.All, pending ? "同步中…" : "全部箱子", false, false);
        foreach (var (bounds, index) in state.Tabs)
        {
            bool disabled = pending && index != state.Selected;
            DrawTab(b, bounds, "", index == state.Selected, disabled);
            bool showNumber = bounds.Width >= 56;
            int size = Math.Min(28, Math.Max(1, bounds.Width - (showNumber ? 32 : 12)));
            int iconX = showNumber ? bounds.X + 6 : bounds.Center.X - size / 2;
            Preview(index).DrawIcon(b, new Rectangle(iconX, bounds.Center.Y - size / 2, size, size), disabled);
            if (!showNumber) continue;
            string number = (index + 1).ToString();
            float scale = Math.Min(0.65f, 22f / Math.Max(1, Game1.smallFont.MeasureString(number).X));
            Utility.drawTextWithShadow(b, number, Game1.smallFont, new Vector2(bounds.X + 36, bounds.Y + 10),
                disabled ? Ui.Muted : Ui.Ink, scale);
        }
        int x = Game1.getMouseX(), y = Game1.getMouseY();
        if (state.Bar.Contains(x, y))
        {
            string hint = state.All.Contains(x, y) ? "全部箱子：搜索、远程与取材设置 · " + Ui.Button(mod.Menu.Settings.KeyboardMenuButton)
                : "LT / RT · PageUp / PageDown 切换箱子";
            foreach (var (bounds, index) in state.Tabs)
                if (bounds.Contains(x, y))
                {
                    var box = state.Boxes[index];
                    hint = $"{Preview(index).Name} · {box.Place} ({box.X},{box.Y})\n第 {index + 1} / {state.Boxes.Count} 个箱子\n" + hint;
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
        float scale = Math.Min(0.75f, Math.Max(1, bounds.Width - 16) / Math.Max(1f, Game1.smallFont.MeasureString(label).X));
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(bounds.X + 8, bounds.Y + 8), disabled ? Ui.Muted : Ui.Ink, scale);
    }
    internal void Reset()
    {
        if (Context.IsMainPlayer) states.ResetAllScreens(); else states.Value = new();
    }
}
