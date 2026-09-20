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
    }
    private readonly PerScreen<State> states = new(() => new());
    internal bool Busy => states.Value.Pending || states.Value.OpenChest != null;
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
    internal void Acquire(List<BoxInfo> boxes, bool crafting, Func<bool> valid, Action complete)
    {
        if (Busy || mod.Network.Loading) { mod.Menu.Notify("正在同步仓储，请稍后再试。"); return; }
        var state = states.Value;
        int generation = ++state.Generation;
        state.Pending = true; state.Started = Environment.TickCount64; state.StillValid = valid;
        mod.Network.Refresh(field: crafting ? "craft" : "remote", leaseBoxes: boxes, acquired: granted =>
        {
            if (generation != state.Generation) { mod.Network.ReleaseLease(); return; }
            if (!granted || !state.Pending || !valid())
            {
                Release();
                if (!granted) mod.Menu.Notify(mod.Network.Message);
                return;
            }
            var chests = boxes.Select(StorageCatalog.Resolve).ToArray();
            if (chests.Any(c => c == null || !StorageCatalog.Allowed(c, Game1.player.UniqueMultiplayerID)
                || !StorageCatalog.Flag(c, crafting ? "craft" : "remote", true) || !StorageCatalog.Available(c)))
            { Release(); mod.Menu.Notify("箱子已经变化，请刷新后重试。"); return; }
            state.Requested = chests.Select(c => c!.GetMutex()).Distinct().ToList();
            void AcquireNext(int index)
            {
                if (!state.Pending || !valid()) { Release(); return; }
                if (index == state.Requested.Count)
                {
                    state.Pending = false;
                    try { complete(); }
                    catch (Exception error) { mod.Report(error); Release(); mod.Menu.Notify("仓储操作失败，请查看 SMAPI 日志。"); }
                    return;
                }
                var mutex = state.Requested[index];
                if (mutex.IsLocked()) { Release(); mod.Menu.Notify("箱子正在使用，请稍后再试。"); return; }
                state.Polling.Add(mutex);
                mutex.RequestLock(() =>
                {
                    state.Polling.Remove(mutex);
                    if (generation != state.Generation) { if (mutex.IsLockHeld()) mutex.ReleaseLock(); return; }
                    if (!state.Pending || !valid()) { if (mutex.IsLockHeld()) mutex.ReleaseLock(); Release(); return; }
                    state.Owned.Add(mutex); AcquireNext(index + 1);
                }, () => { state.Polling.Remove(mutex); if (generation != state.Generation) return;
                    Release(); mod.Menu.Notify("箱子已被其他玩家打开，请稍后再试。"); });
            }
            AcquireNext(0);
        });
    }
    internal void Tick()
    {
        var state = states.Value;
        foreach (var mutex in state.Polling.ToArray()) mutex.Update(Game1.getOnlineFarmers());
        if (state.Pending && (Environment.TickCount64 - state.Started > 15000 || state.StillValid?.Invoke() != true)) Release();
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
        state.Pending = false; state.OpenChest = null;
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
