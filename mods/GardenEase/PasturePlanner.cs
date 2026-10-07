using Microsoft.Xna.Framework;
using StardewValley;

namespace GardenEase;

// 只计算布局，不修改世界。动物建筑的位置决定优先级，动物当前站位不参与排序。
internal static class PasturePlanner
{
    private static readonly Point[] Directions = { new(-1, 0), new(1, 0), new(0, -1), new(0, 1) };
    private sealed record Spot(Vector2 Tile, int Distance, int Growth, bool Existing);

    internal static BatchMove? Plan(Farm farm, ISet<Vector2> reserved)
    {
        var sources = new Dictionary<Vector2, ArrangeItem>();
        CollisionMask groundMask = CollisionMask.All & ~(CollisionMask.TerrainFeatures | CollisionMask.Flooring | CollisionMask.Farmers);
        foreach (var pair in farm.terrainFeatures.Pairs)
        {
            if (!ArrangeItem.IsPasture(pair.Value) || reserved.Contains(pair.Key) || !farm.isTileOnMap(pair.Key)) continue;
            var item = ArrangeItem.From(pair.Value)!;
            if (item.UnavailableReason() == null && !item.HasCollision(farm, pair.Key, groundMask)
                && !LargeTerrain(farm, pair.Key)) sources.Add(pair.Key, item);
        }
        if (sources.Count == 0) return null;

        int width = farm.Map.Layers[0].LayerWidth, height = farm.Map.Layers[0].LayerHeight;
        var walkable = new bool[width, height];
        var natural = new bool[width, height];
        var exemplar = sources.First().Value;
        bool Inside(Point p) => p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var tile = new Vector2(x, y);
                // 寻路忽略角色、动物；允许经过道路和牧草，但不能穿越围栏、树或建筑。
                walkable[x, y] = !farm.IsTileBlockedBy(tile, CollisionMask.All & ~(CollisionMask.Characters | CollisionMask.Farmers),
                    ignorePassables: CollisionMask.All) && farm.doesTileHaveProperty(x, y, "Water", "Back") == null;
                natural[x, y] = !reserved.Contains(tile)
                    && (!farm.terrainFeatures.ContainsKey(tile) || sources.ContainsKey(tile))
                    && farm.doesTileHaveProperty(x, y, "Diggable", "Back") != null
                    && !farm.IsNoSpawnTile(tile) && farm.isTilePlaceable(tile, true)
                    && farm.doesTileHaveProperty(x, y, "TouchAction", "Back") == null
                    && !farm.warps.Any(w => w.X == x && w.Y == y)
                    && !LargeTerrain(farm, tile) && !exemplar.HasCollision(farm, tile, groundMask, checkPassability: true);
            }

        int[,] Distances(IEnumerable<Point> seeds)
        {
            var distances = new int[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) distances[x, y] = int.MaxValue;
            var queue = new Queue<Point>();
            foreach (var seed in seeds)
                if (Inside(seed) && walkable[seed.X, seed.Y] && distances[seed.X, seed.Y] != 0)
                { distances[seed.X, seed.Y] = 0; queue.Enqueue(seed); }
            while (queue.TryDequeue(out var tile))
                foreach (var direction in Directions)
                {
                    var next = tile + direction;
                    if (!Inside(next) || !walkable[next.X, next.Y] || distances[next.X, next.Y] != int.MaxValue) continue;
                    distances[next.X, next.Y] = distances[tile.X, tile.Y] + 1;
                    queue.Enqueue(next);
                }
            return distances;
        }

        var zones = new List<int[,]>();
        foreach (var building in farm.buildings.OrderBy(b => b.tileY.Value).ThenBy(b => b.tileX.Value))
        {
            if (building.GetIndoors() is not AnimalHouse || building.daysOfConstructionLeft.Value > 0) continue;
            var door = building.getRectForAnimalDoor();
            if (door.Width <= 0 || door.Height <= 0) continue;
            // 以动物门外沿为起点，围栏内的空地优先，不从动物临时位置另开区域。
            zones.Add(Distances(Enumerable.Range(door.Left / 64, door.Width / 64)
                .Select(x => new Point(x, door.Bottom / 64))));
        }
        var fallbackDistances = Distances(sources.Keys.Select(v => v.ToPoint()));
        var candidates = Enumerable.Range(0, zones.Count + 1).Select(_ => new List<Spot>()).ToArray();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if ((x + y) % 2 != 0 || !natural[x, y]) continue;
                int growth = Directions.Count(d => Inside(new Point(x, y) + d) && natural[x + d.X, y + d.Y]);
                if (growth == 0) continue;
                int zone = zones.Count, distance = int.MaxValue;
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i][x, y] < distance) { zone = i; distance = zones[i][x, y]; }
                if (zone == zones.Count) distance = fallbackDistances[x, y];
                var tile = new Vector2(x, y);
                candidates[zone].Add(new(tile, distance, growth, sources.ContainsKey(tile)));
            }

        // 同一距离带内尽量保留现成草格；多个建筑轮流分配，避免只铺满第一个牧场。
        var queues = candidates.Select(spots => new Queue<Spot>(spots.OrderBy(s => s.Distance / 4)
            .ThenByDescending(s => s.Existing).ThenByDescending(s => s.Growth).ThenBy(s => s.Distance)
            .ThenBy(s => s.Tile.Y).ThenBy(s => s.Tile.X))).ToArray();
        var targets = new List<Vector2>();
        bool progress = true;
        while (targets.Count < sources.Count && progress)
        {
            progress = false;
            for (int i = 0; i < zones.Count && targets.Count < sources.Count; i++)
                if (queues[i].TryDequeue(out var spot)) { targets.Add(spot.Tile); progress = true; }
        }
        while (targets.Count < sources.Count && queues[^1].TryDequeue(out var spot)) targets.Add(spot.Tile);

        // 目标已有草时保留原实例；剩余目标均为空地。空间不足的多余草不参与搬移。
        var retained = targets.Where(sources.ContainsKey).ToHashSet();
        var moving = sources.Where(p => !retained.Contains(p.Key))
            .OrderByDescending(p => ((int)p.Key.X + (int)p.Key.Y) % 2)
            .ThenBy(p => p.Key.Y).ThenBy(p => p.Key.X).ToArray();
        var emptyTargets = targets.Where(t => !retained.Contains(t)).ToArray();
        var entries = emptyTargets.Select((target, i) => new BatchMove.Entry(moving[i].Key, target, moving[i].Value)).ToArray();
        return entries.Length == 0 ? null : BatchMove.ForPasture(farm, entries);
    }

    private static bool LargeTerrain(Farm farm, Vector2 tile) => farm.resourceClumps.Any(c => c.occupiesTile((int)tile.X, (int)tile.Y))
        || farm.largeTerrainFeatures?.Any(f => f.getBoundingBox().Intersects(new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))) == true;
}
