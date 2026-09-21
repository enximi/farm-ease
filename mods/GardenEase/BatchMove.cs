using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Network;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace GardenEase;

// A whole-layout plan: validation reads a virtual result, never temporarily
// removing objects from the live farm just to calculate a preview.
internal sealed class BatchMove
{
    internal const int MaxTiles = 256;
    internal sealed record Entry(Vector2 From, Vector2 To, ArrangeItem Item);
    internal sealed record Check(string? Reason, HashSet<Vector2> Conflicts);
    internal Rectangle Area { get; }
    internal Entry[] Entries { get; }
    private readonly Dictionary<(bool Objects, Vector2 Tile), object?> sources;
    internal Vector2 Origin => new(Area.X, Area.Y);
    internal Vector2[] Affected => Entries.SelectMany(e => new[] { e.From, e.To }).Distinct().ToArray();

    private BatchMove(Rectangle area, Entry[] entries, Dictionary<(bool, Vector2), object?> sources)
    { Area = area; Entries = entries; this.sources = sources; }

    internal static Rectangle Between(Vector2 a, Vector2 b) => new((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y),
        (int)Math.Abs(a.X - b.X) + 1, (int)Math.Abs(a.Y - b.Y) + 1);
    internal static bool ValidArea(Farm farm, Rectangle area) => area.Width > 0 && area.Height > 0
        && (long)area.Width * area.Height <= MaxTiles && area.X >= 0 && area.Y >= 0
        && (long)area.X + area.Width <= farm.Map.Layers[0].LayerWidth
        && (long)area.Y + area.Height <= farm.Map.Layers[0].LayerHeight;
    internal static IEnumerable<Vector2> Tiles(Rectangle area)
    {
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++) yield return new(x, y);
    }
    private static object? Read(Farm farm, bool objects, Vector2 tile) => objects
        ? farm.objects.TryGetValue(tile, out var obj) ? obj : null
        : farm.terrainFeatures.TryGetValue(tile, out var ground) ? ground : null;
    private static Dictionary<(bool, Vector2), object?> Snapshot(Farm farm, IEnumerable<Vector2> tiles)
    {
        var result = new Dictionary<(bool, Vector2), object?>();
        foreach (Vector2 tile in tiles.Distinct())
            foreach (bool objects in new[] { false, true }) result[(objects, tile)] = Read(farm, objects, tile);
        return result;
    }
    private static bool LargeTerrain(Farm farm, Vector2 tile) => farm.resourceClumps.Any(c => c.occupiesTile((int)tile.X, (int)tile.Y))
        || farm.largeTerrainFeatures?.Any(f => f.getBoundingBox().Intersects(new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))) == true;

    internal static string Select(Farm farm, Rectangle area, out BatchMove? batch)
    {
        batch = null;
        if (!ValidArea(farm, area)) return $"框选需在农场内，一次最多 {MaxTiles} 格（如 16 × 16）。";
        var entries = new List<Entry>();
        foreach (var tile in Tiles(area))
        {
            var ground = ArrangeItem.Read(farm, tile, false);
            var obj = ArrangeItem.Read(farm, tile, true);
            if ((Read(farm, false, tile) != null && ground == null)
                || (Read(farm, true, tile) != null && obj == null && ground?.Tapper == null) || LargeTerrain(farm, tile))
                return $"({(int)tile.X},{(int)tile.Y}) 有暂不支持的地形或物体，请缩小框选范围。";
            if (ground != null) entries.Add(new(tile, tile, ground));
            if (obj != null) entries.Add(new(tile, tile, obj));
        }
        if (entries.Count == 0) return "这片区域没有可以整理的对象。";
        foreach (var entry in entries)
        {
            if (entry.Item.UnavailableReason() is string reason) return $"({(int)entry.From.X},{(int)entry.From.Y})：{reason}";
            if (farm.IsTileOccupiedBy(entry.From, CollisionMask.All & ~(CollisionMask.TerrainFeatures | CollisionMask.Flooring | CollisionMask.Objects | CollisionMask.Farmers)))
                return "选区内有建筑、动物或其他障碍，请先移开。";
        }
        batch = new(area, entries.ToArray(), Snapshot(farm, entries.Select(e => e.From)));
        return $"已选中 {entries.Count} 个对象，移动光标后确认整批搬移；原位置暂不改变。";
    }
    internal BatchMove At(Vector2 origin)
    {
        Vector2 offset = origin - Origin;
        return new(Area, Entries.Select(e => e with { To = e.From + offset }).ToArray(), sources);
    }
    internal Rectangle DestinationArea => new((int)(Area.X + Entries[0].To.X - Entries[0].From.X),
        (int)(Area.Y + Entries[0].To.Y - Entries[0].From.Y), Area.Width, Area.Height);
    internal BatchMove Reverse(Farm farm) => new(DestinationArea, Entries.Select(e => new Entry(e.To, e.From, e.Item)).ToArray(),
        Snapshot(farm, Entries.Select(e => e.To)));

    private static IEnumerable<(bool Objects, object Value)> Components(ArrangeItem item)
    {
        yield return (item.ObjectLayer, item.Value);
        if (item.Tapper != null) yield return (true, item.Tapper);
    }
    internal Check Validate(Farm farm, Farmer actor, ISet<NetMutex>? locks = null)
    {
        var conflicts = new HashSet<Vector2>();
        string? first = null;
        void Fail(Vector2 tile, string reason)
        { conflicts.Add(tile); first ??= $"({(int)tile.X},{(int)tile.Y})：{reason}"; }
        if (Entries.All(e => e.From == e.To)) return new("移动光标选择整批放置位置。", conflicts);
        if (!ValidArea(farm, DestinationArea)) return new("整组选区超出农场边界。", Entries.Select(e => e.To).ToHashSet());
        foreach (var pair in sources)
            if (!ReferenceEquals(pair.Value, Read(farm, pair.Key.Objects, pair.Key.Tile))) Fail(pair.Key.Tile, "原位置已变化，请取消后重新框选。");

        var removed = new Dictionary<(bool, Vector2), object>();
        var added = new Dictionary<(bool, Vector2), object>();
        foreach (var entry in Entries)
        {
            if (!entry.Item.IsAt(farm, entry.From)) Fail(entry.From, "原对象或采集器已变化。");
            if (entry.Item.UnavailableReason(locks) is string unavailable) Fail(entry.From, unavailable);
            if (LargeTerrain(farm, entry.From) || farm.IsTileOccupiedBy(entry.From,
                CollisionMask.All & ~(CollisionMask.TerrainFeatures | CollisionMask.Flooring | CollisionMask.Objects | CollisionMask.Farmers)))
                Fail(entry.From, "原位置有障碍或动物。");
            foreach (var (objects, value) in Components(entry.Item))
            { removed[(objects, entry.From)] = value; added[(objects, entry.To)] = value; }
        }
        foreach (var entry in Entries)
            foreach (var (objects, _) in Components(entry.Item))
            {
                var current = Read(farm, objects, entry.To);
                if (current != null && (!removed.TryGetValue((objects, entry.To), out var leaving) || !ReferenceEquals(current, leaving)))
                    Fail(entry.To, "目标有选区外的对象；批量移动不交换或覆盖。" );
            }
        object? Final(bool objects, Vector2 tile) => added.TryGetValue((objects, tile), out var value) ? value
            : removed.ContainsKey((objects, tile)) ? null : Read(farm, objects, tile);
        TerrainFeature? Ground(Vector2 tile) => Final(false, tile) as TerrainFeature;
        SObject? ObjectAt(Vector2 tile) => Final(true, tile) as SObject;
        foreach (var entry in Entries)
        {
            var item = entry.Item; Vector2 tile = entry.To;
            if (LargeTerrain(farm, tile)) Fail(tile, "这里有大型地形。");
            if (!farm.isTilePlaceable(tile, item.PassableFor(actor))) Fail(tile, "这里是水域或地图限制区域。");
            if (item.Dirt != null && farm.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Diggable", "Back") == null)
                Fail(tile, "这里不能耕种。");
            if (item.Object?.IsSprinkler() == true && farm.doesTileHavePropertyNoNull((int)tile.X, (int)tile.Y, "NoSprinklers", "Back") == "T")
                Fail(tile, "这里不能放置洒水器。");
            var ground = Ground(tile); var obj = ObjectAt(tile);
            if (item.IsTree)
            {
                if (!ReferenceEquals(obj, item.Tapper)) Fail(tile, "树木不能与其他物体重叠。");
                if (TreePlacement.Invalid(farm, tile, item, null, Ground, ObjectAt) is string treeReason) Fail(tile, treeReason);
            }
            else if (item.ObjectLayer)
            {
                if (ground != null && !ArrangeItem.AllowsObject(ground))
                    Fail(tile, "设施下方有作物、树木或其他地形。");
            }
            else if (obj != null && (!added.ContainsKey((true, tile)) || (item.Dirt?.crop != null)))
                Fail(tile, "目标上方有物体；地面只能与选区内设施一起搬移。");
            CollisionMask mask = CollisionMask.All & ~(CollisionMask.Objects | CollisionMask.TerrainFeatures | CollisionMask.Flooring);
            if (item.PassableFor(actor)) mask &= ~CollisionMask.Farmers;
            if (farm.IsTileBlockedBy(tile, mask)) Fail(tile, "这里有角色、建筑、动物或障碍。");
        }
        return new(first, conflicts);
    }

    internal void Transfer(Farm farm, Farmer actor)
    {
        if (!Context.IsMainPlayer) throw new InvalidOperationException("只有房主可以修改农场布局。");
        var mutexes = Entries.SelectMany(e => e.Item.Mutexes()).Distinct().ToArray();
        var owned = new HashSet<NetMutex>();
        void Acquire(int index)
        {
            if (index == mutexes.Length)
            {
                if (Validate(farm, actor, owned).Reason is string reason) throw new InvalidOperationException(reason);
                TransferCore(farm); return;
            }
            var mutex = mutexes[index];
            mutex.Update(farm.farmers);
            if (mutex.IsLocked()) throw new InvalidOperationException("选区内的箱子正在使用，整批未搬移。");
            bool complete = false;
            try
            {
                mutex.RequestLock(() => { owned.Add(mutex); Acquire(index + 1); complete = true; });
                mutex.Update(farm.farmers);
                if (!complete) throw new InvalidOperationException("未取得全部箱子锁，整批未搬移。");
            }
            finally
            {
                owned.Remove(mutex);
                if (mutex.IsLockHeld() || !mutex.IsLocked()) mutex.ReleaseLock();
            }
        }
        Acquire(0);
    }
    private void TransferCore(Farm farm)
    {
        var lights = new MoveLights(farm);
        foreach (var entry in Entries) lights.Capture(entry.Item, entry.From, entry.To);
        try
        {
            lights.Detach();
            foreach (var entry in Entries) entry.Item.Remove(farm, entry.From);
            foreach (var entry in Entries) entry.Item.Add(farm, entry.To);
            foreach (var entry in Entries) entry.Item.Refresh(farm, entry.To);
            lights.Apply(restore: false);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            Attempt(lights.Detach);
            foreach (var entry in Entries) Attempt(() => entry.Item.RemoveOwned(farm, entry.To));
            foreach (var entry in Entries) Attempt(() => entry.Item.Restore(farm, entry.From));
            foreach (var entry in Entries) Attempt(() => entry.Item.Refresh(farm, entry.From));
            Attempt(() => lights.Apply(restore: true));
            if (errors.Count > 1) throw new AggregateException("批量搬移失败，恢复过程中也发生错误。", errors);
            throw;
        }
    }
}
