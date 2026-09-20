using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace StorageEase;

public sealed class BoxInfo
{
    public string Id { get; set; } = "";
    public string Location { get; set; } = "";
    public bool Structure { get; set; }
    public string Root { get; set; } = "";
    public string Place { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Name { get; set; } = "";
    public long Owner { get; set; }
    public bool Remote { get; set; }
    public bool Craft { get; set; }
    public bool Shared { get; set; }
}

internal static class StorageCatalog
{
    internal const string Prefix = "zzz.StorageEase/";
    internal static bool Supported(Chest chest) => chest.GetType() == typeof(Chest) && chest.playerChest.Value
        && !chest.fridge.Value && !chest.giftbox.Value && chest.GlobalInventoryId == null
        && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest or Chest.SpecialChestTypes.JunimoChest;
    internal static long Owner(Chest chest) => chest.owner.Value != 0 ? chest.owner.Value : Game1.MasterPlayer.UniqueMultiplayerID;
    internal static bool Flag(Chest chest, string key, bool fallback) => chest.modData.TryGetValue(Prefix + key, out var value)
        ? value == "true" : fallback;
    internal static bool Allowed(Chest chest, long player) => Owner(chest) == player || Flag(chest, "shared", false);
    internal static bool Available(Chest chest) => !chest.isTemporarilyInvisible && chest.localKickStartTile == null;
    internal static IEnumerable<(GameLocation Location, Chest Chest)> All()
    {
        var boxes = new List<(GameLocation, Chest)>();
        Utility.ForEachLocation(location =>
        {
            foreach (var pair in location.objects.Pairs)
                if (pair.Value is Chest chest && Supported(chest)) boxes.Add((location, chest));
            return true;
        }, includeInteriors: true, includeGenerated: false);
        return boxes;
    }
    internal static List<BoxInfo> List(long player)
    {
        var result = new List<BoxInfo>();
        var ids = new HashSet<string>();
        foreach (var (location, chest) in All())
        {
            if (!chest.modData.TryGetValue(Prefix + "id", out string id) || !ids.Add(id))
            {
                if (!Context.IsMainPlayer) continue;
                chest.modData[Prefix + "id"] = id = Guid.NewGuid().ToString("N");
                ids.Add(id);
            }
            if (!Allowed(chest, player)) continue;
            result.Add(new BoxInfo
            {
                Id = id, Location = location.NameOrUniqueName, Structure = location.isStructure.Value,
                Root = StorageNetwork.GameNetwork.locationRoot(location)?.Value.NameOrUniqueName ?? location.NameOrUniqueName,
                Place = location.DisplayName, X = (int)chest.TileLocation.X, Y = (int)chest.TileLocation.Y,
                Name = chest.modData.TryGetValue(Prefix + "name", out var label) ? label : chest.DisplayName,
                Owner = Owner(chest), Remote = Flag(chest, "remote", true), Craft = Flag(chest, "craft", true), Shared = Flag(chest, "shared", false)
            });
        }
        return result.OrderBy(box => box.Place).ThenBy(box => box.Y).ThenBy(box => box.X).ToList();
    }
    internal static Chest? Resolve(BoxInfo info)
    {
        var location = Game1.getLocationFromName(info.Location, info.Structure);
        return location?.objects.TryGetValue(new Vector2(info.X, info.Y), out var obj) == true && obj is Chest chest
            && Supported(chest) && chest.modData.TryGetValue(Prefix + "id", out string id) && id == info.Id ? chest : null;
    }
    internal static bool InComfortArea() => Game1.currentLocation is Farm or FarmHouse;
}
