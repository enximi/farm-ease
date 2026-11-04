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
    public string IconId { get; set; } = "";
    public bool Remote { get; set; }
    public bool Craft { get; set; }
}

internal static class StorageCatalog
{
    internal const string Prefix = "zzz.StorageEase/";
    internal static bool Supported(Chest chest) => chest.GetType() == typeof(Chest) && chest.playerChest.Value
        && !chest.fridge.Value && !chest.giftbox.Value && chest.GlobalInventoryId == null
        && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest or Chest.SpecialChestTypes.JunimoChest;
    internal static bool Flag(Chest chest, string key, bool fallback) => chest.modData.TryGetValue(Prefix + key, out var value)
        ? value == "true" : fallback;
    internal static bool Available(Chest chest) => UnavailableReason(chest) == null;
    internal static string? UnavailableReason(Chest chest)
    {
        if (chest.isTemporarilyInvisible) return "箱子正在移动或隐藏，请等操作结束。";
        if (chest.Location?.objects.TryGetValue(chest.TileLocation, out var placed) != true || !ReferenceEquals(placed, chest))
            return "箱子位置正在同步，请稍后刷新箱子列表。";
        // 推动先更新地图中的实际位置，再播放本屏动画；远处地图不推进这个动画。
        // 分屏的另一屏可能一直保留 localKickStartTile，不能据此永久禁用已落位的远程箱子。
        // 不清除动画或改写联网字段，仍由原版处理当前地图上的推动效果。
        if (ReferenceEquals(chest.Location, Game1.currentLocation) && chest.localKickStartTile != null)
            return "箱子正在被推动，请等推动动画结束。";
        return null;
    }
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
    internal static List<BoxInfo> List()
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
            var preview = StoragePreview.From(chest);
            result.Add(new BoxInfo
            {
                Id = id, Location = location.NameOrUniqueName, Structure = location.isStructure.Value,
                Root = StorageNetwork.GameNetwork.locationRoot(location)?.Value.NameOrUniqueName ?? location.NameOrUniqueName,
                Place = location.DisplayName, X = (int)chest.TileLocation.X, Y = (int)chest.TileLocation.Y,
                Name = preview.Name, IconId = preview.IconId,
                Remote = Flag(chest, "remote", true), Craft = Flag(chest, "craft", true)
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
