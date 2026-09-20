using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;

namespace StorageEase;

internal sealed class StorageAccess
{
    private readonly ModEntry mod;
    private sealed class State
    {
        internal bool Pending;
        internal long Started;
        internal Func<bool>? StillValid;
        internal List<NetMutex> Requested = new();
        internal readonly HashSet<NetMutex> Polling = new();
        internal readonly HashSet<NetMutex> Owned = new();
        internal int Generation;
        internal Chest? OpenChest;
        internal Action? CancelPending;
    }
    private readonly PerScreen<State> states = new(() => new());
    internal bool Busy => Pending || states.Value.OpenChest != null;
    internal bool Pending => states.Value.Pending || states.Value.Polling.Count > 0;
    internal StorageAccess(ModEntry mod) => this.mod = mod;
    internal void Open(BoxInfo info)
    {
        if (!mod.InRange) { mod.Menu.Notify("舒适模式仅在农场或农舍内使用。可以切换随行模式。"); return; }
        if (!info.Remote) { mod.Menu.Notify("这个箱子未开启远程访问。"); return; }
        var browser = Game1.activeClickableMenu;
        Acquire(new List<BoxInfo> { info }, false, () => ReferenceEquals(Game1.activeClickableMenu, browser) && mod.InRange, () =>
        {
            var chest = StorageCatalog.Resolve(info);
            if (chest == null) { Release(); return; }
            states.Value.OpenChest = chest;
            chest.ShowMenu();
        });
    }
    internal void Switch(ItemGrabMenu menu, BoxInfo info)
    {
        if (menu.context is not Chest previous || !mod.InRange || !info.Remote) return;
        if (menu.heldItem != null || !menu.readyToClose())
        { mod.Menu.Notify("请先放下手中的物品，再切换箱子。"); return; }
        var target = StorageCatalog.Resolve(info);
        if (target == null) { mod.Menu.Notify("这个箱子已经移动或尚未同步，请稍后重试。"); return; }
        if (ReferenceEquals(previous, target)) return;
        if (!previous.GetMutex().IsLockHeld()) { mod.Menu.Notify("当前箱子的使用状态已变化，请重新打开。"); return; }
        if (target.GetMutex().IsLocked() && !ReferenceEquals(target.GetMutex(), previous.GetMutex()))
        { mod.Menu.Notify("这个箱子正在使用，请稍后再试。"); return; }
        bool Valid() => mod.InRange && ReferenceEquals(Game1.activeClickableMenu, menu)
            && menu.heldItem == null && menu.readyToClose() && previous.GetMutex().IsLockHeld();
        Acquire(new() { info }, false, Valid, () =>
        {
            var chest = StorageCatalog.Resolve(info);
            if (chest == null || !chest.GetMutex().IsLockHeld()) { states.Value.CancelPending?.Invoke(); return; }
            int? selected = menu.currentlySnappedComponent?.myID;
            // Keep the original menu and lock until the destination is granted.
            // Closing the old menu first also runs its native held-item cleanup.
            menu.exitThisMenu(false);
            states.Value.OpenChest = chest;
            chest.ShowMenu();
            foreach (var mutex in states.Value.Owned.Where(m => !ReferenceEquals(m, chest.GetMutex())).ToArray())
            {
                if (mutex.IsLockHeld()) mutex.ReleaseLock();
                states.Value.Owned.Remove(mutex);
            }
            if (!ReferenceEquals(previous.GetMutex(), chest.GetMutex()) && previous.GetMutex().IsLockHeld())
                previous.GetMutex().ReleaseLock();
            if (Game1.activeClickableMenu is ItemGrabMenu next && selected is int id && next.getComponentWithID(id) is { } component)
            {
                next.currentlySnappedComponent = component;
                if (Game1.options.SnappyMenus) next.snapCursorToCurrentSnappedComponent();
            }
            Game1.playSound("smallSelect");
        }, previous);
    }
    internal void Acquire(List<BoxInfo> boxes, bool crafting, Func<bool> valid, Action complete, Chest? replacing = null)
    {
        var state = states.Value;
        if (Pending || (state.OpenChest != null && !ReferenceEquals(state.OpenChest, replacing)) || mod.Network.Loading)
        { mod.Menu.Notify("正在同步仓储，请稍后再试。"); return; }
        bool keepOpen = state.OpenChest != null;
        var retained = state.Owned.ToHashSet();
        void Cancel()
        {
            if (!keepOpen) { Release(); return; }
            state.Generation++; state.Pending = false; state.CancelPending = null;
            foreach (var mutex in state.Owned.Where(m => !retained.Contains(m)).ToArray())
            {
                if (mutex.IsLockHeld()) mutex.ReleaseLock();
                state.Owned.Remove(mutex);
            }
        }
        int generation = ++state.Generation;
        state.Pending = true; state.Started = Environment.TickCount64; state.StillValid = valid;
        state.CancelPending = Cancel;
        mod.Network.Refresh(field: crafting ? "craft" : "remote", leaseBoxes: boxes, acquired: granted =>
        {
            if (generation != state.Generation) return;
            if (!granted || !state.Pending || !valid())
            {
                Cancel();
                if (!granted) mod.Menu.Notify(mod.Network.Message);
                return;
            }
            var chests = boxes.Select(StorageCatalog.Resolve).ToArray();
            if (chests.Any(c => c == null || !StorageCatalog.Allowed(c, Game1.player.UniqueMultiplayerID)
                || !StorageCatalog.Flag(c, crafting ? "craft" : "remote", true) || !StorageCatalog.Available(c)))
            { Cancel(); mod.Menu.Notify("箱子已经变化，请刷新后重试。"); return; }
            state.Requested = chests.Select(c => c!.GetMutex()).Distinct().ToList();
            void AcquireNext(int index)
            {
                if (!state.Pending || !valid()) { Cancel(); return; }
                if (index == state.Requested.Count)
                {
                    if (boxes.Where((box, i) => !ReferenceEquals(StorageCatalog.Resolve(box), chests[i])).Any()
                        || chests.Any(c => !c!.GetMutex().IsLockHeld() || !StorageCatalog.Available(c)
                            || !StorageCatalog.Allowed(c, Game1.player.UniqueMultiplayerID)
                            || !StorageCatalog.Flag(c, crafting ? "craft" : "remote", true)))
                    { Cancel(); mod.Menu.Notify("箱子状态或权限已变化，请重试。"); return; }
                    state.Pending = false;
                    try { complete(); state.CancelPending = null; }
                    catch (Exception error) { mod.Report(error); Release(); mod.Menu.Notify("仓储操作失败，请查看 SMAPI 日志。"); }
                    return;
                }
                var mutex = state.Requested[index];
                // Multiple Junimo chests can expose the same already-owned mutex.
                if (replacing != null && ReferenceEquals(mutex, replacing.GetMutex()) && mutex.IsLockHeld())
                { state.Owned.Add(mutex); AcquireNext(index + 1); return; }
                if (mutex.IsLocked()) { Cancel(); mod.Menu.Notify("箱子正在使用，请稍后再试。"); return; }
                state.Polling.Add(mutex);
                mutex.RequestLock(() =>
                {
                    state.Polling.Remove(mutex);
                    if (generation != state.Generation) { if (mutex.IsLockHeld()) mutex.ReleaseLock(); return; }
                    if (!state.Pending || !valid()) { if (mutex.IsLockHeld()) mutex.ReleaseLock(); Cancel(); return; }
                    state.Owned.Add(mutex); AcquireNext(index + 1);
                }, () => { state.Polling.Remove(mutex); if (generation != state.Generation) return;
                    Cancel(); mod.Menu.Notify("箱子已被其他玩家打开，请稍后再试。"); });
            }
            AcquireNext(0);
        });
    }
    internal void Tick()
    {
        var state = states.Value;
        foreach (var mutex in state.Polling.ToArray()) mutex.Update(Game1.getOnlineFarmers());
        if (state.Pending && (Environment.TickCount64 - state.Started > 15000 || state.StillValid?.Invoke() != true)) state.CancelPending?.Invoke();
        if (state.OpenChest is { } chest && (!mod.InRange || !StorageCatalog.Available(chest)
            || !chest.GetMutex().IsLockHeld() || !IsChestMenu(Game1.activeClickableMenu, chest)))
        {
            if (IsChestMenu(Game1.activeClickableMenu, chest)) Game1.activeClickableMenu.exitThisMenu();
            Release();
        }
    }
    private static bool IsChestMenu(IClickableMenu? menu, Chest chest) => menu is ItemGrabMenu grab && ReferenceEquals(grab.context, chest);
    internal void Release()
    {
        var state = states.Value;
        state.Generation++;
        state.Pending = false; state.OpenChest = null; state.CancelPending = null;
        // Never release another player's lock or a lock owned by a native workbench.
        foreach (var mutex in state.Owned.ToArray()) if (mutex.IsLockHeld()) mutex.ReleaseLock();
        state.Owned.Clear();
        // A queued acquisition can still arrive after cancellation. Keep callbacks
        // attached so they release that late grant rather than leaking a lock.
        state.Requested.RemoveAll(m => !m.IsLocked());
        mod.Network.ReleaseLease();
    }
    internal void Reset()
    {
        Release();
        if (StardewModdingAPI.Context.IsMainPlayer) states.ResetAllScreens(); else states.Value = new();
    }
}
