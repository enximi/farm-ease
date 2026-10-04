using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;

namespace StorageEase;

// A menu is a viewer. Only a single, revalidated input holds the native mutex.
// Native code continues to own inventory transfers, including cursor cleanup.
internal sealed class StorageAccess
{
    private readonly ModEntry mod;
    private sealed class State
    {
        internal bool Pending, Executing, RemoteOpen;
        internal long Started;
        internal Func<bool>? StillValid;
        internal readonly HashSet<NetMutex> Polling = new(), Owned = new();
        internal int Generation, SettleTicks;
        internal Action? Ready;
        internal Chest? OpenChest;
        internal BoxInfo? OpenInfo;
        internal Microsoft.Xna.Framework.Vector2 OpenTile;
        internal ItemGrabMenu? TrackedMenu;
    }
    private readonly PerScreen<State> states = new(() => new());
    internal bool Busy => Pending || states.Value.OpenChest != null;
    internal bool Pending => states.Value.Pending || states.Value.Polling.Count > 0;
    internal StorageAccess(ModEntry mod) => this.mod = mod;
    internal static bool IsSupportedMenu(IClickableMenu? current) => current is ItemGrabMenu menu
        && menu.GetType() == typeof(ItemGrabMenu) && menu.source == ItemGrabMenu.source_chest
        && menu.context is Chest chest && ReferenceEquals(menu.sourceItem, chest) && StorageCatalog.Supported(chest);
    internal bool Shared(ItemGrabMenu menu) => mod.Network.Compatible && IsSupportedMenu(menu);

    internal void TrackMenu()
    {
        if (!IsSupportedMenu(Game1.activeClickableMenu) || !mod.Network.Compatible) return;
        var menu = (ItemGrabMenu)Game1.activeClickableMenu;
        var chest = (Chest)menu.context;
        var state = states.Value;
        if (ReferenceEquals(state.TrackedMenu, menu)) return;
        bool first = !ReferenceEquals(state.OpenChest, chest);
        if (first)
        {
            state.RemoteOpen = false;
            state.OpenTile = chest.TileLocation;
            state.OpenInfo = StorageCatalog.List().FirstOrDefault(b => ReferenceEquals(StorageCatalog.Resolve(b), chest));
        }
        state.OpenChest = chest; state.TrackedMenu = menu;
        // Convert a vanilla physical opening too, without releasing a transaction
        // lock when Chest.ShowMenu rebuilds the menu during a transfer.
        if (!state.Pending && !state.Executing && chest.GetMutex().IsLockHeld()) chest.GetMutex().ReleaseLock();
        mod.Navigation.Remember(chest);
        if (first && !mod.Network.Loading) mod.Network.Refresh();
        menu.exitFunction += () =>
        {
            if (state.Executing || !ReferenceEquals(state.TrackedMenu, menu)) return;
            Release();
        };
    }
    internal bool PhysicalOpen(Chest chest, Farmer who, bool checking, ref bool result)
    {
        if (!mod.Network.Compatible || !StorageCatalog.Supported(chest) || who != Game1.player || checking) return true;
        if (!Game1.didPlayerJustRightClick(ignoreNonMouseHeldInput: true)) { result = false; return false; }
        if (!StorageCatalog.Available(chest) || Pending) { result = true; return false; }
        Game1.player.Halt();
        chest.ShowMenu();
        Game1.playSound("openChest");
        result = true;
        return false;
    }
    internal void Open(BoxInfo info)
    {
        if (!mod.InRange) { mod.Menu.Notify("舒适模式仅在农场或农舍内使用。可以切换随行模式。"); return; }
        if (Pending) return;
        var chest = StorageCatalog.Resolve(info);
        if (!info.Remote || chest == null || !StorageCatalog.Available(chest) || !StorageCatalog.Flag(chest, "remote", true))
        { mod.Menu.Notify("箱子已移动或关闭远程访问，请刷新后重试。"); return; }
        chest.ShowMenu();
        states.Value.RemoteOpen = true;
        states.Value.OpenInfo = info;
    }
    internal void Switch(ItemGrabMenu menu, BoxInfo info)
    {
        if (Pending || !mod.InRange || !info.Remote) return;
        if (menu.heldItem != null || !menu.readyToClose())
        { mod.Menu.Notify("请先放下手中的物品，再切换箱子。"); return; }
        if (StorageCatalog.Resolve(info) is not { } target || !StorageCatalog.Available(target)
            || !StorageCatalog.Flag(target, "remote", true))
        { mod.Menu.Notify("这个箱子已经移动或关闭远程访问，请刷新后重试。"); return; }
        int? selected = menu.currentlySnappedComponent?.myID;
        menu.exitThisMenu(false);
        Open(info);
        if (Game1.activeClickableMenu is ItemGrabMenu next && selected is int id && next.getComponentWithID(id) is { } component)
        {
            next.currentlySnappedComponent = component;
            if (Game1.options.SnappyMenus) next.snapCursorToCurrentSnappedComponent();
        }
        Game1.playSound("smallSelect");
    }

    // Do not replay coordinates against a changed inventory or a resized menu.
    // These are identity/count stamps, never inventories which can grant items.
    private static Func<bool> Stamp(IEnumerable<Item> items)
    {
        var original = items.Select(i => (Item: i, Count: i?.Stack ?? 0, Quality: i?.Quality ?? 0)).ToArray();
        return () => original.SequenceEqual(items.Select(i => (Item: i, Count: i?.Stack ?? 0, Quality: i?.Quality ?? 0)));
    }
    internal bool Input(ItemGrabMenu menu, Action input, int? clickX = null, int? clickY = null)
    {
        if (!Shared(menu) || states.Value.Executing) return true;
        if (Pending) return false;
        TrackMenu();
        var state = states.Value;
        var chest = (Chest)menu.context;
        var info = state.OpenInfo ?? StorageCatalog.List().FirstOrDefault(b => ReferenceEquals(StorageCatalog.Resolve(b), chest));
        if (info == null || mod.Network.Loading)
        { mod.Menu.Notify("正在同步箱子，请稍后再操作。"); return false; }
        int slot = clickX is int cx && clickY is int cy
            ? menu.ItemsToGrabMenu.inventory.FindIndex(component => component.containsPoint(cx, cy)) : -1;
        var inventory = chest.GetItemsForPlayer();
        Item? AtSlot() => slot >= 0 && slot < inventory.Count ? inventory[slot] : null;
        var clicked = AtSlot();
        int quality = clicked?.Quality ?? 0;
        // Keep the intended stack, but allow taking its latest remaining count.
        // Sorting/removal/replacement of that slot cancels instead of retargeting.
        bool SameSlot() => slot < 0 || (ReferenceEquals(AtSlot(), clicked) && (clicked?.Quality ?? 0) == quality);
        bool shift = Game1.oldKBState.IsKeyDown(Keys.LeftShift), control = Game1.oldKBState.IsKeyDown(Keys.LeftControl);
        var backpack = Stamp(Game1.player.Items);
        var cursor = menu.heldItem;
        int cursorCount = cursor?.Stack ?? 0;
        int x = menu.xPositionOnScreen, y = menu.yPositionOnScreen, width = Game1.uiViewport.Width, height = Game1.uiViewport.Height;
        bool Valid() => ReferenceEquals(Game1.activeClickableMenu, menu) && ReferenceEquals(StorageCatalog.Resolve(info), chest)
            && StorageCatalog.Available(chest) && (!state.RemoteOpen || mod.InRange)
            && ReferenceEquals(menu.heldItem, cursor) && (cursor?.Stack ?? 0) == cursorCount
            && Game1.oldKBState.IsKeyDown(Keys.LeftShift) == shift && Game1.oldKBState.IsKeyDown(Keys.LeftControl) == control
            && menu.xPositionOnScreen == x && menu.yPositionOnScreen == y && Game1.uiViewport.Width == width && Game1.uiViewport.Height == height;
        Acquire(new() { info }, false, Valid, () =>
        {
            try
            {
                if (!SameSlot() || !backpack())
                { mod.Menu.Notify("物品已变化，这次操作已取消，请重新选择。"); return; }
                state.Executing = true;
                input();
                TrackMenu();
            }
            finally { state.Executing = false; FinishOperation(); }
        }, chest, physical: !state.RemoteOpen);
        return false;
    }

    internal void Acquire(List<BoxInfo> boxes, bool crafting, Func<bool> valid, Action complete, Chest? replacing = null, bool physical = false)
    {
        var state = states.Value;
        if (Pending || (state.OpenChest != null && !ReferenceEquals(state.OpenChest, replacing)) || mod.Network.Loading)
        { mod.Menu.Notify("正在同步仓储，请稍后再试。"); return; }
        int generation = ++state.Generation;
        state.Pending = true; state.Started = Environment.TickCount64; state.StillValid = valid;
        string field = crafting ? "craft" : physical ? "physical" : "remote";
        bool Allowed(Chest c) => StorageCatalog.Available(c) && (physical || StorageCatalog.Flag(c, field, true));
        mod.Network.Refresh(field: field, leaseBoxes: boxes, acquired: granted =>
        {
            if (generation != state.Generation) return;
            if (!granted || !state.Pending || !valid())
            { FinishOperation(); if (!granted) mod.Menu.Notify(mod.Network.Message); return; }
            var chests = boxes.Select(StorageCatalog.Resolve).ToArray();
            if (chests.Any(c => c == null || !Allowed(c)))
            { FinishOperation(); mod.Menu.Notify("箱子已经变化，请刷新后重试。"); return; }
            var requested = chests.Select(c => c!.GetMutex()).Distinct().ToArray();
            void Cancel(string message)
            { FinishOperation(); mod.Menu.Notify(message); }
            void AcquireNext(int index)
            {
                if (generation != state.Generation || !state.Pending || !valid()) { FinishOperation(); return; }
                if (index == requested.Length)
                {
                    // Mutex owner fields bypass interpolation. Wait for preceding
                    // inventory deltas to finish interpolation before revalidation.
                    state.SettleTicks = Context.IsMultiplayer ? StorageNetwork.GameNetwork.interpolationTicks() + 2 : 0;
                    state.Ready = () =>
                    {
                        if (!valid() || boxes.Where((box, i) => !ReferenceEquals(StorageCatalog.Resolve(box), chests[i])).Any()
                            || chests.Any(c => !c!.GetMutex().IsLockHeld() || !Allowed(c)))
                        { Cancel("箱子状态或开关已变化，请重试。"); return; }
                        state.Pending = false;
                        try { complete(); }
                        catch (Exception error) { mod.Report(error); FinishOperation(); mod.Menu.Notify("仓储操作失败，请查看 SMAPI 日志。"); }
                    };
                    return;
                }
                var mutex = requested[index];
                // 远程箱子不一定逐帧更新：释放后 owner 已清空，prevOwner 可能仍是旧玩家。
                // 必须先处理旧状态，再注册本次回调，否则旧解锁会误触发本次失败回调；
                // 房主也可能因新旧持有者相同而收不到成功回调。使用在线玩家保留远程操作锁。
                mutex.Update(Game1.getOnlineFarmers());
                if (generation != state.Generation || !state.Pending || !valid()) { FinishOperation(); return; }
                // A native workbench may already own one of these. Never adopt
                // that lock; the workbench is responsible for its lifetime.
                if (mutex.IsLockHeld()) { AcquireNext(index + 1); return; }
                if (mutex.IsLocked()) { Cancel("箱子正在执行其他操作，请重试。"); return; }
                state.Polling.Add(mutex);
                mutex.RequestLock(() =>
                {
                    state.Polling.Remove(mutex);
                    if (generation != state.Generation || !state.Pending || !valid())
                    {
                        if (mutex.IsLockHeld()) mutex.ReleaseLock();
                        if (state.Polling.Count == 0) mod.Network.ReleaseLease();
                        return;
                    }
                    state.Owned.Add(mutex); AcquireNext(index + 1);
                }, () =>
                {
                    state.Polling.Remove(mutex);
                    if (generation == state.Generation) Cancel("箱子正在执行其他操作，请重试。");
                    else if (state.Polling.Count == 0) mod.Network.ReleaseLease();
                });
            }
            AcquireNext(0);
        });
    }
    internal void Tick()
    {
        var state = states.Value;
        TrackMenu();
        foreach (var mutex in state.Polling.ToArray()) mutex.Update(Game1.getOnlineFarmers());
        if (state.Pending && (Environment.TickCount64 - state.Started > 15000 || state.StillValid?.Invoke() != true)) FinishOperation();
        if (state.Ready != null && --state.SettleTicks <= 0)
        {
            var ready = state.Ready; state.Ready = null; ready();
        }
        if (state.OpenChest != null && state.OpenInfo == null)
            state.OpenInfo = StorageCatalog.List().FirstOrDefault(b => ReferenceEquals(StorageCatalog.Resolve(b), state.OpenChest));
        if (state.OpenChest is { } chest && ((state.RemoteOpen && (!mod.InRange || !StorageCatalog.Flag(chest, "remote", true)))
            || !StorageCatalog.Available(chest) || chest.TileLocation != state.OpenTile
            || chest.Location?.objects.TryGetValue(state.OpenTile, out var placed) != true || !ReferenceEquals(placed, chest)
            || (state.OpenInfo != null && !ReferenceEquals(StorageCatalog.Resolve(state.OpenInfo), chest))
            || !IsSupportedMenu(Game1.activeClickableMenu) || !ReferenceEquals(((ItemGrabMenu)Game1.activeClickableMenu).context, chest)))
        {
            if (Game1.activeClickableMenu is ItemGrabMenu menu && ReferenceEquals(menu.context, chest)) menu.exitThisMenu();
            Release();
        }
    }
    private void FinishOperation()
    {
        var state = states.Value;
        state.Generation++; state.Pending = false; state.Ready = null; state.StillValid = null;
        // Flush inventory + farmer deltas while still locked, then publish the
        // unlock. The host reservation stays until the unlock settles there too.
        try
        {
            if (Context.IsWorldReady && state.Owned.Count > 0) StorageNetwork.GameNetwork.UpdateLate(forceSync: true);
        }
        finally
        {
            foreach (var mutex in state.Owned) if (mutex.IsLockHeld()) mutex.ReleaseLock();
            state.Owned.Clear();
            try { if (Context.IsWorldReady) StorageNetwork.GameNetwork.UpdateLate(forceSync: true); }
            finally
            {
                // A canceled native request can arrive late; keep its lease alive
                // until its callback releases the lock. No new request meanwhile.
                if (state.Polling.Count == 0) mod.Network.ReleaseLease();
            }
        }
    }

    internal void Release()
    {
        FinishOperation();
        var state = states.Value;
        state.OpenChest = null; state.OpenInfo = null; state.TrackedMenu = null; state.RemoteOpen = false;
    }
    internal void Reset()
    {
        Release();
        if (Context.IsMainPlayer) states.ResetAllScreens(); else states.Value = new();
    }
}
