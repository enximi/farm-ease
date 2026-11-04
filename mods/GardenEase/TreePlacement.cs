using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace GardenEase;

internal static class TreePlacement
{
    internal static string? Invalid(GameLocation location, Vector2 target, ArrangeItem incoming, ArrangeItem? outgoing,
        Func<Vector2, TerrainFeature?>? groundAt = null, Func<Vector2, SObject?>? objectAt = null)
    {
        if (!incoming.IsTree) return null;
        int x = (int)target.X, y = (int)target.Y;
        bool allowTrees = location.doesEitherTileOrTileIndexPropertyEqual(x, y, "CanPlantTrees", "Back", "T");
        if (location.IsNoSpawnTile(target, "Tree", ignoreTileSheetProperties: true)
            || (location.IsNoSpawnTile(target, "Tree") && !allowTrees)) return "这格地图不允许种树。";
        if (incoming.Value is FruitTree
            && !allowTrees && location.doesTileHaveProperty(x, y, "Diggable", "Back") == null
            && !(location.IsGreenhouse && location.doesTileHaveProperty(x, y, "Type", "Back") == "Stone")
            && location.doesTileHaveProperty(x, y, "Type", "Back") is not ("Grass" or "Dirt"))
            return "果树需要可种植的泥土、草地或温室地砖。";

        // Evaluate the resulting layout, so moving one tile or swapping doesn't
        // count the tree/tapper that is about to leave its old position.
        Vector2 source = ((TerrainFeature)incoming.Value).Tile;
        TerrainFeature? Ground(Vector2 tile) => groundAt != null ? groundAt(tile) : tile == target ? (TerrainFeature)incoming.Value
            : tile == source ? outgoing?.Value as TerrainFeature
            : location.terrainFeatures.TryGetValue(tile, out var ground) ? ground : null;
        SObject? ObjectAt(Vector2 tile) => objectAt != null ? objectAt(tile) : tile == target ? incoming.Tapper
            : tile == source ? outgoing?.Tapper
            : location.objects.TryGetValue(tile, out var obj) ? obj : null;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var tile = target + new Vector2(dx, dy);
                var ground = Ground(tile);
                if (ground is Tree or FruitTree && (incoming.Value is FruitTree || ground is FruitTree))
                    return "果树与其他树木之间需要留出至少两格间隔。";
                if (incoming.Value is not FruitTree { growthStage.Value: < 4 } || Math.Abs(dx) > 1 || Math.Abs(dy) > 1) continue;
                var obj = ObjectAt(tile);
                if (!location.isTileOnMap(tile) || (obj != null && obj.QualifiedItemId is not ("(O)590" or "(O)SeedSpot"))
                    || (ground != null && ground is not Grass && !(ground is HoeDirt dirt && dirt.crop == null))
                    || location.IsTileOccupiedBy(tile, CollisionMask.Buildings | CollisionMask.Furniture | CollisionMask.LocationSpecific)
                    || location.resourceClumps.Any(clump => clump.occupiesTile((int)tile.X, (int)tile.Y))
                    || (location.largeTerrainFeatures?.Any(feature => feature.getBoundingBox().Intersects(new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64))) == true))
                    return "未成熟果树周围八格需留空，不能有道路、作物或设施；草与空耕地可保留。";
            }
        return null;
    }
}
