using Microsoft.Xna.Framework;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace GardenEase;

internal static class LayoutDisplay
{
    internal static void OnTerrainChanged(object? sender, TerrainFeatureListChangedEventArgs e)
    {
        if (e.Location is not Farm farm) return;
        foreach (var pair in e.Added)
        {
            if (!farm.terrainFeatures.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, pair.Value)
                || current.isTemporarilyInvisible) continue;

            // 原生网络反序列化先创建作物，随后才由地形字典设置 Tile。
            // HoeDirt.Tile 只更新 tilePosition，不刷新非联网的 drawPosition、贴图和绘制层级。
            // 等加入地形后按最终格子刷新本屏缓存，避免 P2 要重新进入地图才能看见作物。
            // 此处仅更新绘制数据，不调用房主的 Refresh，不写浇水、肥料或生长等联网字段。
            if (current is HoeDirt dirt && dirt.GetType() == typeof(HoeDirt)
                && dirt.crop is { } crop && crop.GetType() == typeof(Crop)) crop.updateDrawMath(pair.Key);
            else if (ArrangeItem.IsPasture(current)) ((Grass)current).setUpRandom();
            // 树木的首次纹理选择可能早于下一次地形更新，在首次绘制前补齐季节缓存。
            else if (current is Tree tree && tree.GetType() == typeof(Tree)) tree.performPlayerEntryAction();
            else if (current is FruitTree fruit && fruit.GetType() == typeof(FruitTree)) fruit.loadSprite();
        }

        // 批量移动、交换及撤销先移除再加入；按最终布局重算改动格及周围八格的道路连接。
        // 不调用耕地 updateNeighbors：它会间接计算水稻灌溉并可能写入联网字段。
        var neighbors = new HashSet<Vector2>();
        foreach (var tile in e.Added.Select(p => p.Key).Concat(e.Removed.Select(p => p.Key)))
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++) neighbors.Add(tile + new Vector2(x, y));
        foreach (var tile in neighbors)
            if (farm.terrainFeatures.TryGetValue(tile, out var ground)
                && ground is Flooring floor && floor.GetType() == typeof(Flooring)) floor.OnAdded(farm, tile);
    }

    internal static void OnObjectsChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (e.Location is not Farm farm) return;
        foreach (var pair in e.Added)
        {
            var obj = pair.Value;
            if (ArrangeItem.From(obj) == null || obj.isTemporarilyInvisible
                || !farm.objects.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, obj)) continue;
            // 原生回调已设置主体位置，但不会给围栏附件补充非联网的 Location。
            // 客机仅修复地图关联；附件坐标与灯光由房主移动后通过原生网络同步。
            if (obj is Fence && obj.heldObject.Value is Torch torch) torch.Location = farm;
            if (obj is Chest chest) chest.fixLidFrame();
        }
    }
}
