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
    private static readonly HashSet<string> StableLocations = new()
        { "Farm", "FarmHouse", "BusStop", "Town", "Mountain", "Forest", "Beach", "Mine", "Railroad", "Woods", "Desert", "IslandSouth" };
    internal bool CanReturn => previous != null;
    internal TravelService(ModEntry mod) => this.mod = mod;
    internal void Clear() => previous = null;

    internal bool GoHome()
    {
        if (!mod.CanUse(out string reason, true, allowMounted: true)) { mod.Notify(reason); return false; }
        var entry = Utility.getHomeOfFarmer(Game1.player).getFrontDoorSpot();
        return Go("Farm", new Vector2(entry.X, entry.Y), "家门口");
    }

    internal bool Return() => previous is { } anchor && Go(anchor.Location, anchor.Tile, "上个位置");

    internal bool Go(string name, Vector2 near, string label)
    {
        if (!mod.CanUse(out string reason, true, allowMounted: true)) { mod.Notify(reason); return false; }
        try
        {
            if (TravelDestinations.LockReason(name, near) is string locked)
            { mod.Notify(locked); return false; }
            // Passive festivals can replace destination maps. Don't choose a landing tile on the wrong map.
            if (name != "Farm" && Game1.netWorldState.Value.ActivePassiveFestivals.Any())
            { mod.Notify("特殊活动期间，暂时只开放回家。"); return false; }
            GameLocation? target = Game1.getLocationFromName(name);
            if (target == null || !FindLanding(target, near, out Vector2 landing))
            { mod.Notify(Game1.player.mount != null ? "目的地附近没有能容纳人马的安全落脚点，请先清理障碍。" : "目的地附近没有安全落脚点，请先清理障碍。"); return false; }
            var old = StableLocations.Contains(Game1.currentLocation.NameOrUniqueName) || Game1.currentLocation is FarmHouse
                ? new Anchor(Game1.currentLocation.NameOrUniqueName, Game1.player.Tile) : null;
            Game1.activeClickableMenu?.exitThisMenu(false);
            mod.Mounted.Warp(target, landing);
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
                    // 原版会把最右列落点左移一格，避免校验位置与实际位置不一致。
                    if (tile.X >= location.Map.Layers[0].LayerWidth - 1) continue;
                    var bounds = Rectangle.Union(new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64),
                        MountedTravel.LandingBounds(tile));
                    bool safe = true;
                    for (int ty = (int)Math.Floor(bounds.Top / 64d); ty <= (int)Math.Floor((bounds.Bottom - 1) / 64d) && safe; ty++)
                        for (int tx = (int)Math.Floor(bounds.Left / 64d); tx <= (int)Math.Floor((bounds.Right - 1) / 64d); tx++)
                            if (!IsSafeTile(location, new Vector2(tx, ty))) { safe = false; break; }
                    if (!safe) continue;
                    result = tile;
                    return true;
                }
        result = default;
        return false;
    }

    private static bool IsSafeTile(GameLocation location, Vector2 tile)
    {
        if (!location.isTileOnMap(tile) || location.getTileIndexAt((int)tile.X, (int)tile.Y, "Back") < 0
            || !location.isTilePassable(tile)) return false;
        if (TravelDestinations.LockReason(location.NameOrUniqueName, tile) != null) return false;
        if (location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Water", "Back") != null) return false;
        if (location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "TouchAction", "Back") != null) return false;
        if (location.IsTileOccupiedBy(tile, CollisionMask.All & ~CollisionMask.Farmers)) return false;
        var bounds = new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
        if (location is Forest forest && forest.ShouldTravelingMerchantVisitToday())
        {
            Point cart = forest.GetTravelingMerchantCartTile();
            // 尚未进入森林时货车碰撞框可能未创建，按原版占地提前留空。
            if (new Rectangle(cart.X * 64, cart.Y * 64, 8 * 64, 3 * 64).Intersects(bounds)) return false;
        }
        if (location.farmers.Any(farmer => farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID
            && farmer.GetBoundingBox().Intersects(bounds))) return false;
        // 原版逐格占用对农场动物只比较中心格，补上越过格线的身体碰撞范围。
        if (location.animals.Values.Any(animal => animal.GetBoundingBox().Intersects(bounds))) return false;
        return !location.warps.Any(w => w.X == (int)tile.X && w.Y == (int)tile.Y);
    }
}
