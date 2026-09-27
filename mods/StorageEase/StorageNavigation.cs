using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace StorageEase;

internal sealed class StorageNavigation
{
    private const string LastChestKey = StorageCatalog.Prefix + "lastChest";
    private sealed class State { internal bool Active, ReturnToHub; }
    private readonly PerScreen<State> states = new(() => new());
    private readonly ModEntry mod;
    internal bool Active => states.Value.Active;
    internal StorageNavigation(ModEntry mod) => this.mod = mod;

    internal void Remember(Chest chest)
    {
        if (chest.modData.TryGetValue(StorageCatalog.Prefix + "id", out string id)
            && (!Game1.player.modData.TryGetValue(LastChestKey, out string previous) || previous != id))
            Game1.player.modData[LastChestKey] = id;
    }
    internal BoxInfo? LastBox() => Game1.player.modData.TryGetValue(LastChestKey, out string id)
        ? mod.Network.Boxes.FirstOrDefault(box => box.Id == id && box.Remote) : null;

    internal bool CanQuickOpen() => mod.InRange && !mod.Access.Busy && mod.Menu.CanUse(out _);
    internal void OpenLast(bool fromHub)
    {
        if (!mod.Menu.CanUse(out string reason, true)) { mod.Menu.Notify(reason); return; }
        if (mod.Access.Busy) { mod.Menu.Notify("正在结束上一次箱子操作，请稍候。"); return; }
        Game1.activeClickableMenu?.exitThisMenu(false);
        states.Value = new State { Active = true, ReturnToHub = fromHub };
        if (!mod.InRange)
        {
            mod.Menu.Notify("舒适模式仅在农场或农舍内使用，也可以在设置中改为随行模式。");
            Game1.activeClickableMenu = new StorageOptionsMenu(mod);
            return;
        }
        Game1.activeClickableMenu = new StorageOpeningMenu(mod);
        mod.Network.Refresh();
    }
    internal void ShowBrowser()
    {
        if (!mod.InRange) { mod.Menu.Notify("舒适模式仅在农场或农舍内使用。"); return; }
        if (Game1.activeClickableMenu is ItemGrabMenu menu && (menu.heldItem != null || !menu.readyToClose()))
        { mod.Menu.Notify("请先放下手中的物品，再查看全部箱子。"); return; }
        Game1.activeClickableMenu?.exitThisMenu(false);
        mod.Access.Release();
        states.Value.Active = true;
        Game1.activeClickableMenu = new StorageBrowser(mod);
        mod.Network.Refresh();
    }
    internal void Close()
    {
        Game1.activeClickableMenu?.exitThisMenu();
        mod.Access.Release();
        Finish();
    }
    private void Finish()
    {
        bool returnToHub = states.Value.Active && states.Value.ReturnToHub;
        states.Value = new();
        if (returnToHub && Game1.activeClickableMenu == null && mod.Menu.CanUse(out _)) mod.Menu.Hub?.Open("");
    }
    internal void Tick()
    {
        if (StorageAccess.IsSupportedMenu(Game1.activeClickableMenu)) Remember((Chest)((ItemGrabMenu)Game1.activeClickableMenu).context);
        if (states.Value.Active && Game1.activeClickableMenu == null)
        { mod.Access.Release(); Finish(); }
        else if (states.Value.Active && Game1.activeClickableMenu is GameMenu) states.Value = new();
    }
    internal void Reset()
    {
        if (Context.IsMainPlayer) states.ResetAllScreens(); else states.Value = new();
    }
    internal void CancelReturn() => states.Value = new();
}
