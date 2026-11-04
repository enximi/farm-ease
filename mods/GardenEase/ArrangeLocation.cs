using Microsoft.Xna.Framework;
using StardewValley;

namespace GardenEase;

internal static class ArrangeLocation
{
    // 菜单、房主授权与本地显示共用范围，避免分屏入口可用但房主拒绝操作。
    internal static bool Supports(GameLocation? location) => location is Farm || location?.IsGreenhouse == true;

    internal static bool CanPlace(GameLocation location, Vector2 tile, bool passable)
    {
        if (!location.isTilePlaceable(tile, passable)) return false;
        if (!location.IsGreenhouse) return true;
        int x = (int)tile.X, y = (int)tile.Y;
        // 原版 NoFurniture 分支会读取本屏地图；房主可能在室外，需按正在整理的温室再检查。
        if (location.doesTileHaveProperty(x, y, "NoFurniture", "Back") != null) return false;
        // 温室出口可能位于地图边界外一格，连同门前一格保留通道。
        return !location.warps.Any(warp => Math.Abs(warp.X - x) + Math.Abs(warp.Y - y) <= 1)
            && location.doesTileHaveProperty(x, y, "TouchAction", "Back") == null
            && location.doesTileHaveProperty(x, y, "Action", "Buildings") == null;
    }
}
