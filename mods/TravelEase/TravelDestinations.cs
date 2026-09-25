using FarmMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace TravelEase;

internal static class TravelDestinations
{
    private sealed record Destination(string Id, string Group, string Title, string Location, int X, int Y, string Hint)
    {
        internal Vector2 Tile => new(X, Y);
        internal string? Locked => LockReason(Location, Tile);
    }

    private static readonly Destination[] Places =
    {
        new("bus", "farm", "巴士站", "BusStop", 23, 9, "农场东侧的巴士站。"),
        new("forest", "farm", "森林", "Forest", 58, 17, "森林东北侧，方便前往牧场。"),
        new("cart", "farm", "旅行货车附近", "Forest", 27, 14, "森林西北侧；货车仍按原版日期和时间营业。"),
        new("wizard", "farm", "法师塔门口", "Forest", 5, 28, "落在塔外，进入仍遵守原版剧情和营业时间。"),
        new("town", "town", "小镇", "Town", 43, 57, "鹈鹕镇中心区域。"),
        new("smith", "town", "铁匠铺 / 博物馆", "Town", 94, 83, "小镇东侧，两处建筑仍遵守原版营业时间。"),
        new("beach", "town", "海滩", "Beach", 20, 5, "海滩北侧。"),
        new("pier", "town", "鱼店码头", "Beach", 30, 35, "威利鱼店门外的码头。"),
        new("mountain", "wild", "山上", "Mountain", 31, 20, "山地湖泊附近。"),
        new("mine", "wild", "矿洞", "Mine", 18, 12, "普通矿洞入口大厅；电梯仍使用已解锁楼层。"),
        new("railroad", "wild", "铁路 / 温泉", "Railroad", 10, 58, "温泉入口外，铁路区域开放后可用。"),
        new("quarry", "wild", "采石场", "Mountain", 110, 20, "采石场西侧，桥修复后可用。"),
        new("woods", "wild", "秘密森林", "Woods", 55, 15, "秘密森林入口内侧，入口大木头清除后可用。"),
        new("desert", "far", "沙漠", "Desert", 18, 28, "巴士落客区；巴士修复后免费传送。"),
        new("island", "far", "姜岛码头", "IslandSouth", 21, 43, "修好船并由当前玩家正常登岛一次后，免费传送到码头。")
    };

    internal static void Register(IFarmMenuApi api, TravelService travel)
    {
        var groups = new[] { ("farm", "农场周边"), ("town", "小镇"), ("wild", "野外"), ("far", "远方") };
        for (int i = 0; i < groups.Length; i++)
            api.RegisterSection("travel.destinations." + groups[i].Item1, "travel.destinations", groups[i].Item2,
                "选择落点；未解锁的目的地会显示开放条件。", i * 10);
        api.RegisterAction("travel.destinations.farm", "travel.farm", () => "家门口",
            () => "回到当前住宅门外，自动避开障碍。", () => travel.GoHome(), () => true, -10);
        for (int i = 0; i < Places.Length; i++)
        {
            var place = Places[i];
            api.RegisterAction("travel.destinations." + place.Group, "travel.destination." + place.Id,
                () => place.Title + (place.Locked == null ? "" : "（未解锁）"),
                () => place.Locked ?? place.Hint,
                () => travel.Go(place.Location, place.Tile, place.Title), () => place.Locked == null, i * 10);
        }
    }

    // The same checks run at submission and when returning to a recorded point,
    // including the quarry which shares the Mountain map with unlocked areas.
    internal static string? LockReason(string location, Vector2 tile)
    {
        if (!Context.IsWorldReady) return "请先进入存档。";
        if (location == "Railroad" && Game1.stats.DaysPlayed < Mountain.daysBeforeLandslide)
            return "铁路与温泉尚未开放，等待第一年夏季地震后通路打开。";
        if (location == "Mountain" && tile.X >= 100
            && !Utility.doesMasterPlayerHaveMailReceivedButNotMailForTomorrow("ccCraftsRoom"))
            return "请先通过社区中心或 Joja 修复通往采石场的桥。";
        if (location == "Woods")
        {
            if (Game1.getLocationFromName("Forest") is not Forest forest)
                return "暂时无法确认秘密森林入口状态。";
            if (forest.resourceClumps.Any(clump => clump.occupiesTile(1, 6) || clump.occupiesTile(1, 7)))
                return "请先清除秘密森林入口的大木头。";
        }
        if (location == "Desert" && !Game1.isLocationAccessible("Desert"))
            return "请先通过社区中心或 Joja 修复巴士。";
        if (location == "IslandSouth")
        {
            if (!Game1.MasterPlayer.mailReceived.Contains("willyBoatFixed"))
                return "请先修好威利的船，等待修复完成。";
            if (!Game1.player.hasOrWillReceiveMail("Visited_Island"))
                return "请先由当前玩家乘船登上姜岛一次。";
        }
        return null;
    }
}
