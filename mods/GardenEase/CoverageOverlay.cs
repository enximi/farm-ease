using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace GardenEase;

internal static class CoverageOverlay
{
    internal sealed record Area(HashSet<Vector2> Tiles, Color Color);
    internal static string Hint(ArrangeItem? item) => item?.Object is not { } obj ? ""
        : obj.IsSprinkler() ? "蓝色：洒水范围（含喷头加成）"
        : obj.IsScarecrow() ? "金色：稻草人保护范围" : "";

    internal static void Draw(SpriteBatch b, Farm farm, ArrangeItem? item, Vector2 center, bool valid, bool secondary = false)
    {
        if (Footprint(farm, item, center) is { } area) DrawArea(b, area, valid, secondary);
    }
    internal static Area[] BatchAreas(Farm farm, IEnumerable<BatchMove.Entry> entries)
    {
        var areas = new Dictionary<Color, HashSet<Vector2>>();
        foreach (var entry in entries)
            if (Footprint(farm, entry.Item, entry.To) is { } area)
            {
                if (!areas.TryGetValue(area.Color, out var tiles)) areas[area.Color] = tiles = new();
                tiles.UnionWith(area.Tiles);
            }
        return areas.Select(pair => new Area(pair.Value, pair.Key)).ToArray();
    }
    private static Area? Footprint(Farm farm, ArrangeItem? item, Vector2 center)
    {
        if (item?.Object is not { } obj) return null;
        var tiles = new HashSet<Vector2>();
        Color color;
        if (obj.IsSprinkler())
        {
            color = new Color(95, 185, 255);
            // Translate the game's actual footprint, including pressure nozzles,
            // without assigning the live sprinkler a temporary position.
            foreach (Vector2 tile in obj.GetSprinklerTiles())
            {
                Vector2 target = tile - obj.TileLocation + center;
                if (farm.isTileOnMap(target) && farm.doesTileHavePropertyNoNull((int)target.X, (int)target.Y, "NoSprinklers", "Back") != "T")
                    tiles.Add(target);
            }
        }
        else if (obj.IsScarecrow())
        {
            color = new Color(255, 207, 105);
            int radius = obj.GetRadiusForScarecrow();
            // Farm's crow check uses distance < radius, not a square or <= radius.
            int minX = Math.Max(0, (int)center.X - radius), maxX = Math.Min(farm.Map.Layers[0].LayerWidth - 1, (int)center.X + radius);
            int minY = Math.Max(0, (int)center.Y - radius), maxY = Math.Min(farm.Map.Layers[0].LayerHeight - 1, (int)center.Y + radius);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var tile = new Vector2(x, y);
                    if (Vector2.DistanceSquared(tile, center) < (float)radius * radius) tiles.Add(tile);
                }
        }
        else return null;
        return new(tiles, color);
    }
    internal static void DrawArea(SpriteBatch b, Area area, bool valid, bool secondary = false)
    {
        var tiles = area.Tiles;
        Color color = area.Color;
        if (!valid) color = new Color(244, 107, 99);
        float opacity = secondary ? 0.55f : 1;
        foreach (Vector2 tile in tiles)
        {
            var r = new Rectangle((int)tile.X * 64 - Game1.viewport.X, (int)tile.Y * 64 - Game1.viewport.Y, 64, 64);
            if (r.Right <= 0 || r.Bottom <= 0 || r.Left >= Game1.viewport.Width || r.Top >= Game1.viewport.Height) continue;
            b.Draw(Game1.staminaRect, r, color * (0.12f * opacity));
            Color border = color * (0.7f * opacity);
            if (!tiles.Contains(tile - Vector2.UnitX)) b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, 2, 64), border);
            if (!tiles.Contains(tile + Vector2.UnitX)) b.Draw(Game1.staminaRect, new Rectangle(r.Right - 2, r.Y, 2, 64), border);
            if (!tiles.Contains(tile - Vector2.UnitY)) b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, 64, 2), border);
            if (!tiles.Contains(tile + Vector2.UnitY)) b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - 2, 64, 2), border);
        }
    }
}
