using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Network;
using StardewModdingAPI;

namespace GardenEase;

internal sealed class MoveSession
{
    internal Farm Farm { get; }
    internal Vector2? Source { get; private set; }
    internal ArrangeItem? Selected { get; private set; }
    internal BatchMove? Batch { get; private set; }
    internal bool HasSelection => Selected != null || Batch != null;
    internal IEnumerable<Vector2> ReservedTiles => Batch != null
        ? Batch.Entries.Where(e => e.Item.IsAt(Farm, e.From)).Select(e => e.From).Distinct()
        : Source is Vector2 source && Selected?.IsAt(Farm, source) == true ? new[] { source } : Array.Empty<Vector2>();
    internal Vector2[] LastChanged { get; private set; } = Array.Empty<Vector2>();
    private readonly Farmer actor;
    private readonly HashSet<NetMutex> ownedLocks = new();
    private readonly Stack<Move> undo = new();
    private sealed record Move(Vector2 From, Vector2 To, ArrangeItem? Item, ArrangeItem? Swapped, BatchMove? Batch = null)
    {
        internal Vector2[] Tiles => Batch?.Affected ?? new[] { From, To };
    }
    internal int UndoCount => undo.Count;
    internal Vector2[] UndoTiles => !HasSelection && undo.TryPeek(out var move) ? move.Tiles : Array.Empty<Vector2>();
    private bool undoInvalidated;
    internal MoveSession(Farm farm, Farmer? actor = null) { Farm = farm; this.actor = actor ?? Game1.player; }

    internal void InvalidateUndo(ISet<Vector2> changed)
    {
        // Another editor owns the newer layout. Even if an object is moved back
        // later, an old undo must not reverse that player's work.
        var retained = undo.Where(move => !move.Tiles.Any(changed.Contains)).ToArray();
        if (retained.Length == undo.Count) return;
        undoInvalidated = true;
        undo.Clear();
        foreach (var move in retained.Reverse()) undo.Push(move);
    }

    internal string Select(Vector2 tile)
    {
        // A tree and its tapper move together. Other objects take priority over ground.
        var item = ArrangeItem.Read(Farm, tile);
        if (item == null) return "请选择耕地、树木、果树、道路或支持的农场设施。";
        if (item.UnavailableReason() is string reason) return reason;
        if (HasLargeTerrain(tile)) return "这格上有大型地形，暂不能搬移。";
        CollisionMask mask = CollisionMask.All & ~(CollisionMask.TerrainFeatures | CollisionMask.Flooring | CollisionMask.Farmers);
        if (item.PlacedObject != null) mask &= ~CollisionMask.Objects;
        if (Farm.IsTileOccupiedBy(tile, mask)) return "这格上还有物体或动物，请先移开。";
        Source = tile;
        Selected = item;
        return $"已选中{item.Name}，移动光标后确认。";
    }

    internal string SelectBatch(Rectangle area)
    {
        Cancel();
        string result = BatchMove.Select(Farm, area, out var batch);
        Batch = batch;
        return result;
    }
    internal BatchMove.Check CheckBatch(Vector2 target) => Batch?.At(target).Validate(Farm, actor)
        ?? new("请先框选需要搬移的区域。", new());
    internal string PlaceBatch(Vector2 target)
    {
        if (Batch == null) return "请先框选需要搬移的区域。";
        var plan = Batch.At(target);
        if (plan.Validate(Farm, actor).Reason is string reason) return reason + " 整批未搬移。";
        plan.Transfer(Farm, actor);
        LastChanged = plan.Affected;
        undo.Push(new Move(plan.Origin, target, null, null, plan.Reverse(Farm)));
        Cancel();
        return $"已整体搬移 {plan.Entries.Length} 个对象，状态保留；可一步撤销。";
    }

    internal ArrangeItem? SwapTarget(Vector2 tile)
    {
        if (Selected == null || Source == tile) return null;
        var target = ArrangeItem.Read(Farm, tile, Selected.ObjectLayer);
        return target != null && Selected.CanSwapWith(target) ? target : null;
    }

    internal string? InvalidTarget(Vector2 tile)
    {
        if (Source is not Vector2 source || Selected == null) return "请先选择需要整理的对象。";
        if (source == tile) return "请选择另一格，或取消选中。";
        if (!Selected.IsAt(Farm, source)) return "原位置已发生变化，请取消后重新选择。";
        if (Selected.UnavailableReason() is string unavailable) return unavailable;
        var swapped = SwapTarget(tile);
        if (InvalidPlacement(tile, Selected, swapped) is string reason) return reason;
        if (swapped != null && InvalidPlacement(source, swapped, Selected) is string sourceReason)
            return "无法交换回原位置：" + sourceReason;
        return null;
    }

    private bool HasLargeTerrain(Vector2 tile)
    {
        var bounds = new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
        return Farm.resourceClumps.Any(clump => clump.occupiesTile((int)tile.X, (int)tile.Y))
            || (Farm.largeTerrainFeatures != null && Farm.largeTerrainFeatures.Any(feature => feature.getBoundingBox().Intersects(bounds)));
    }

    private string? InvalidPlacement(Vector2 tile, ArrangeItem incoming, ArrangeItem? outgoing)
    {
        if (!Farm.isTileOnMap(tile)) return "超出农场边界。";
        if (incoming.UnavailableReason(ownedLocks) is string unavailable) return unavailable;
        if (outgoing?.UnavailableReason(ownedLocks) is string outgoingUnavailable) return outgoingUnavailable;
        if (!ReferenceEquals(incoming.At(Farm, tile), outgoing?.Value))
            return "这里已有其他对象；耕地、树木、道路和设施只能在各自类别内交换。";
        if (outgoing != null && !outgoing.IsAt(Farm, tile)) return "目标对象或树上采集器已发生变化，请重新选择。";
        if (HasLargeTerrain(tile)) return "这里有大型地形，不能放置。";
        if (!Farm.isTilePlaceable(tile, incoming.PassableFor(actor))) return "这里不能放置，可能是水域或地图限制区域。";
        if (incoming.Dirt != null && Farm.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Diggable", "Back") == null)
            return "这里不能耕种。";
        if (incoming.Object?.IsSprinkler() == true && Farm.doesTileHavePropertyNoNull((int)tile.X, (int)tile.Y, "NoSprinklers", "Back") == "T")
            return "这里不能放置洒水器。";

        CollisionMask mask = CollisionMask.All;
        if (incoming.IsTree)
        {
            if (!ReferenceEquals(Farm.objects.TryGetValue(tile, out var attachment) ? attachment : null, outgoing?.Tapper))
                return "这里有其他物体，不能放置树木。";
            mask &= ~(CollisionMask.Objects | CollisionMask.TerrainFeatures | CollisionMask.Flooring);
            if (TreePlacement.Invalid(Farm, tile, incoming, outgoing) is string treeReason) return treeReason;
        }
        else if (incoming.ObjectLayer)
        {
            // Only empty dirt and flooring may remain underneath a moved object.
            if (Farm.terrainFeatures.TryGetValue(tile, out var ground)
                && !(ground.GetType() == typeof(Flooring) || (ground.GetType() == typeof(HoeDirt) && ((HoeDirt)ground).crop == null)))
                return "这里有作物、树木或其他地形，请先移开。";
            mask &= ~(CollisionMask.Objects | CollisionMask.TerrainFeatures | CollisionMask.Flooring);
        }
        else if (outgoing != null)
            mask &= ~(CollisionMask.TerrainFeatures | CollisionMask.Flooring);
        if (incoming.PassableFor(actor)) mask &= ~CollisionMask.Farmers;
        else if (Farm.IsTileOccupiedBy(tile, CollisionMask.Farmers))
            return "这里有角色，挡路的作物或物体不能放在角色脚下。";
        if (Farm.IsTileBlockedBy(tile, mask)) return "这里有障碍、建筑、物体或动物。";
        return null;
    }

    internal string Place(Vector2 target)
    {
        if (Source is not Vector2 source || Selected == null) return Select(target);
        if (!Selected.IsAt(Farm, source)) { Cancel(); return "原位置已发生变化，已取消选中。"; }
        if (InvalidTarget(target) is string reason) return reason;
        var item = Selected;
        var swapped = SwapTarget(target);
        Transfer(source, target, item, swapped);
        undo.Push(new Move(source, target, item, swapped));
        Cancel();
        return swapped == null ? $"已搬移{item.Name}，原有状态已保留。" : "已交换，双方的原有状态均已保留。";
    }

    internal string Undo(out Vector2? restored)
    {
        restored = null;
        if (HasSelection) { Cancel(); return "已取消当前选中；再次按撤销可恢复上一步。"; }
        if (!undo.TryPeek(out Move? move)) return undoInvalidated
            ? "相关位置已被其他玩家整理，旧撤销记录已失效。" : "当前整理会话还没有搬移记录。";
        if (move.Batch is { } batch)
        {
            if (batch.Validate(Farm, actor).Reason is string batchReason) return "整批暂时无法撤销：" + batchReason;
            batch.Transfer(Farm, actor);
            LastChanged = batch.Affected;
            undo.Pop(); restored = move.From;
            return $"已撤销整批搬移，恢复 {batch.Entries.Length} 个对象的位置。";
        }
        var item = move.Item!;
        if (!item.IsAt(Farm, move.To) || !ReferenceEquals(item.At(Farm, move.From), move.Swapped?.Value)
            || (move.Swapped != null && !move.Swapped.IsAt(Farm, move.From)))
            return "对象位置发生变化，无法安全撤销。";
        if (InvalidPlacement(move.From, item, move.Swapped) is string reason) return "原位置暂时无法恢复：" + reason;
        if (move.Swapped != null && InvalidPlacement(move.To, move.Swapped, item) is string otherReason)
            return "另一格暂时无法恢复：" + otherReason;
        Transfer(move.To, move.From, item, move.Swapped);
        undo.Pop();
        restored = move.From;
        return move.Swapped == null ? "已恢复上一次搬移。" : "已撤销交换，两格均已恢复。";
    }

    private void Transfer(Vector2 source, Vector2 target, ArrangeItem item, ArrangeItem? swapped)
    {
        if (!Context.IsMainPlayer) throw new InvalidOperationException("只有房主可以修改农场布局。");
        var mutexes = item.Mutexes().Concat(swapped?.Mutexes() ?? Enumerable.Empty<NetMutex>()).Distinct().ToArray();
        void Acquire(int index)
        {
            if (index == mutexes.Length)
            {
                CollisionMask sourceMask = CollisionMask.All & ~(CollisionMask.TerrainFeatures | CollisionMask.Flooring | CollisionMask.Farmers);
                if (item.PlacedObject != null) sourceMask &= ~CollisionMask.Objects;
                if (HasLargeTerrain(source) || Farm.IsTileOccupiedBy(source, sourceMask))
                    throw new InvalidOperationException("原位置出现了障碍或动物，请稍后再试。");
                if (InvalidPlacement(target, item, swapped) is string reason) throw new InvalidOperationException(reason);
                if (swapped != null && InvalidPlacement(source, swapped, item) is string otherReason) throw new InvalidOperationException(otherReason);
                TransferCore(source, target, item, swapped);
                return;
            }
            var mutex = mutexes[index];
            // On the host, polling handles queued native chest-open requests before
            // acquiring our lock. The transaction completes inside the acquired callback.
            mutex.Update(Farm.farmers);
            if (mutex.IsLocked()) throw new InvalidOperationException("箱子正在使用，请稍后再试。");
            bool completed = false;
            try
            {
                mutex.RequestLock(() => { ownedLocks.Add(mutex); Acquire(index + 1); completed = true; });
                mutex.Update(Farm.farmers);
                if (!completed) throw new InvalidOperationException("未取得箱子锁，请稍后再试。");
            }
            finally
            {
                ownedLocks.Remove(mutex);
                if (mutex.IsLockHeld() || !mutex.IsLocked()) mutex.ReleaseLock();
            }
        }
        Acquire(0);
        LastChanged = new[] { source, target };
    }

    private void TransferCore(Vector2 source, Vector2 target, ArrangeItem item, ArrangeItem? swapped)
    {
        if (source == target || !item.IsAt(Farm, source) || !ReferenceEquals(item.At(Farm, target), swapped?.Value)
            || (swapped != null && !swapped.IsAt(Farm, target)))
            throw new InvalidOperationException("对象位置已发生变化，操作已停止。");
        var lights = new MoveLights(Farm);
        lights.Capture(item, source, target);
        if (swapped != null) lights.Capture(swapped, target, source);
        // Do not call tool/removal/placement actions: those can drop inventory,
        // reset production, detach sprinkler upgrades or restart machine processing.
        try
        {
            lights.Detach();
            item.Remove(Farm, source);
            swapped?.Remove(Farm, target);
            item.Add(Farm, target);
            swapped?.Add(Farm, source);
            item.Refresh(Farm, target);
            swapped?.Refresh(Farm, source);
            lights.Apply(restore: false);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            Attempt(() => lights.Detach());
            Attempt(() => item.RemoveOwned(Farm, target));
            if (swapped != null) Attempt(() => swapped.RemoveOwned(Farm, source));
            Attempt(() => item.Restore(Farm, source));
            if (swapped != null) Attempt(() => swapped.Restore(Farm, target));
            Attempt(() => item.Refresh(Farm, source));
            if (swapped != null) Attempt(() => swapped.Refresh(Farm, target));
            Attempt(() => lights.Apply(restore: true));
            if (errors.Count > 1) throw new AggregateException("搬移失败，恢复过程中也发生错误。", errors);
            throw;
        }
    }

    internal void Cancel() { Source = null; Selected = null; Batch = null; }
}
