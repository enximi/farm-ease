using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace GardenEase;

internal static class CropDisplay
{
    internal static void OnTerrainChanged(object? sender, TerrainFeatureListChangedEventArgs e)
    {
        if (e.Location is not Farm farm) return;
        foreach (var pair in e.Added)
        {
            if (pair.Value is not HoeDirt dirt || dirt.GetType() != typeof(HoeDirt)
                || dirt.crop is not { } crop || crop.GetType() != typeof(Crop)) continue;
            if (!farm.terrainFeatures.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, dirt)) continue;

            // 原生网络反序列化先创建作物，随后才由地形字典设置 Tile。
            // HoeDirt.Tile 只更新 tilePosition，不刷新非联网的 drawPosition、贴图和绘制层级。
            // 等加入地形后按最终格子刷新本屏缓存，避免 P2 要重新进入地图才能看见作物。
            // 此处仅更新绘制数据，不调用房主的 Refresh，不写浇水、肥料或生长等联网字段。
            crop.updateDrawMath(pair.Key);
        }
    }
}
