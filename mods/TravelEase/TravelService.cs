using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewModdingAPI.Utilities;

namespace TravelEase;

internal sealed class TravelService
{
    private readonly ModEntry mod;
    private readonly PerScreen<Anchor?> anchors = new();
    private Anchor? previous { get => anchors.Value; set => anchors.Value = value; }
    private sealed record Anchor(string Location, Vector2 Tile);
    private static readonly HashSet<string> StableLocations = new() { "Farm", "FarmHouse", "BusStop", "Town", "Mountain", "Forest", "Beach" };
    internal bool CanReturn => previous != null;
    internal TravelService(ModEntry mod) => this.mod = mod;
    internal void Clear() => previous = null;

    internal bool GoHome()
    {
        if (!mod.CanUse(out string reason, true)) { mod.Notify(reason); return false; }
        var entry = Utility.getHomeOfFarmer(Game1.player).getFrontDoorSpot();
        return Go("Farm", new Vector2(entry.X, entry.Y), "家门口");
    }

    internal bool Return() => previous is { } anchor && Go(anchor.Location, anchor.Tile, "上个位置");

    internal bool Go(string name, Vector2 near, string label)
    {
        if (!mod.CanUse(out string reason, true)) { mod.Notify(reason); return false; }
        try
        {
            // Passive festivals can replace destination maps. Don't choose a landing tile on the wrong map.
            if (name != "Farm" && Game1.netWorldState.Value.ActivePassiveFestivals.Any())
            { mod.Notify("特殊活动期间，暂时只开放回家。"); return false; }
            GameLocation? target = Game1.getLocationFromName(name);
            if (target == null || !FindLanding(target, near, out Vector2 landing))
            { mod.Notify("目的地附近没有安全落脚点，请先清理障碍。"); return false; }
            var old = StableLocations.Contains(Game1.currentLocation.NameOrUniqueName) || Game1.currentLocation is FarmHouse
                ? new Anchor(Game1.currentLocation.NameOrUniqueName, Game1.player.Tile) : null;
            Game1.activeClickableMenu?.exitThisMenu(false);
            Game1.warpFarmer(name, (int)landing.X, (int)landing.Y, 2, target.isStructure.Value);
            previous = old;
            mod.Monitor.Log($"传送至 {label} ({name} {landing.X},{landing.Y})", StardewModdingAPI.LogLevel.Debug);
            return true;
        }
        catch (Exception ex) { mod.Report(ex); mod.Notify("传送未完成，请查看 SMAPI 日志。"); return false; }
    }

    private static bool FindLanding(GameLocation location, Vector2 near, out Vector2 result)
    {
        for (int radius = 0; radius <= 8; radius++)
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
                    Vector2 tile = near + new Vector2(x, y);
                    if (!location.isTileOnMap(tile) || !location.isTilePassable(tile)) continue;
                    if (location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Water", "Back") != null) continue;
                    if (location.IsTileOccupiedBy(tile, CollisionMask.All & ~CollisionMask.Farmers)) continue;
                    var bounds = new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
                    if (location.farmers.Any(farmer => farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID
                        && farmer.GetBoundingBox().Intersects(bounds))) continue;
                    if (location.warps.Any(w => w.X == (int)tile.X && w.Y == (int)tile.Y)) continue;
                    result = tile;
                    return true;
                }
        result = default;
        return false;
    }
}
