using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Network;
using SObject = StardewValley.Object;

namespace GardenEase;

// Keep the placed instance, including its inventory, attachments and processing state.
internal sealed class ArrangeItem
{
    internal object Value { get; }
    internal SObject? Object => Value as SObject;
    internal HoeDirt? Dirt => Value as HoeDirt;
    internal bool IsTree => Value is Tree or FruitTree;
    internal SObject? Tapper { get; }
    internal SObject? PlacedObject => Object ?? Tapper;
    internal bool ObjectLayer => Object != null;
    internal bool Passable => PassableFor(Game1.player);
    internal bool PassableFor(Farmer actor) => Value is TerrainFeature terrain ? terrain.isPassable(actor) : Object!.isPassable();
    internal string Name => Object?.DisplayName ?? (Value is FruitTree fruitTree ? fruitTree.GetDisplayName() + "果树"
        : Value is Tree ? (Tapper == null ? "树木" : "树木与采集器")
        : Value is Flooring ? "道路 / 地板" : Dirt?.crop != null ? "作物与耕地" : "空耕地");
    private ArrangeItem(object value, SObject? tapper = null) { Value = value; Tapper = tapper; }

    internal static ArrangeItem? Read(Farm farm, Vector2 tile, bool? objectLayer = null)
    {
        farm.objects.TryGetValue(tile, out var obj);
        farm.terrainFeatures.TryGetValue(tile, out var terrain);
        bool tree = terrain?.GetType() == typeof(Tree) || terrain?.GetType() == typeof(FruitTree);
        if (objectLayer != true && tree && (obj == null
            || (terrain is Tree && obj.GetType() == typeof(SObject) && obj.IsTapper())))
            return new ArrangeItem(terrain!, obj);
        return From(objectLayer == false ? terrain : objectLayer == true ? obj : obj ?? (object?)terrain);
    }

    internal static ArrangeItem? From(object? value)
    {
        if (value is HoeDirt dirt && dirt.GetType() == typeof(HoeDirt)
            && (dirt.crop == null || dirt.crop.GetType() == typeof(Crop))) return new(value);
        if (value is Flooring floor && floor.GetType() == typeof(Flooring)) return new(value);
        if (value is Fence fence && fence.GetType() == typeof(Fence)) return new(value);
        if (value is Chest chest && chest.GetType() == typeof(Chest) && chest.playerChest.Value && !chest.fridge.Value
            && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest
                or Chest.SpecialChestTypes.JunimoChest or Chest.SpecialChestTypes.AutoLoader or Chest.SpecialChestTypes.MiniShippingBin)
            return new(value);
        if (value is SObject obj && obj.GetType() == typeof(SObject) && !obj.IsTapper()
            && (obj.IsSprinkler() || obj.IsScarecrow() || (obj.bigCraftable.Value && obj.GetMachineData() != null)))
            return new(value);
        return null;
    }

    internal bool CanSwapWith(ArrangeItem other) => ObjectLayer ? other.ObjectLayer
        : IsTree ? other.IsTree : Value is HoeDirt ? other.Value is HoeDirt
        : Value is Flooring && other.Value is Flooring;

    internal IEnumerable<NetMutex> Mutexes()
    {
        var seen = new HashSet<SObject>();
        for (SObject? obj = PlacedObject; obj != null && seen.Add(obj); obj = obj.heldObject.Value)
            if (obj is Chest chest) yield return chest.GetMutex();
    }
    internal string? UnavailableReason(ISet<NetMutex>? ownedLocks = null)
    {
        if (Mutexes().Any(mutex => mutex.IsLocked() && !(ownedLocks?.Contains(mutex) == true && mutex.IsLockHeld())))
            return "箱子或附属容器正在使用，请关闭后再整理。";
        if (PlacedObject?.isTemporarilyInvisible == true || Value is TerrainFeature { isTemporarilyInvisible: true })
            return "对象正在移动或隐藏，请稍后再整理。";
        if (Value is Tree tree && (tree.falling.Value || tree.destroy.Value || tree.health.Value <= -99)
            || Value is FruitTree fruit && (fruit.falling.Value || fruit.destroy || fruit.health.Value <= -99))
            return "树木正在倒下或已被破坏，请稍后重新选择。";
        return null;
    }

    internal object? At(Farm farm, Vector2 tile) => ObjectLayer
        ? farm.objects.TryGetValue(tile, out var obj) ? obj : null
        : farm.terrainFeatures.TryGetValue(tile, out var terrain) ? terrain : null;
    internal bool IsAt(Farm farm, Vector2 tile) => ReferenceEquals(At(farm, tile), Value)
        && (!IsTree || ReferenceEquals(farm.objects.TryGetValue(tile, out var obj) ? obj : null, Tapper));
    internal void Remove(Farm farm, Vector2 tile)
    {
        if (!IsAt(farm, tile)) throw new InvalidOperationException("原位置已发生变化。");
        if (Tapper != null && !farm.objects.Remove(tile)) throw new InvalidOperationException("树上采集器已发生变化。");
        bool removed = ObjectLayer ? farm.objects.Remove(tile) : farm.terrainFeatures.Remove(tile);
        if (!removed) throw new InvalidOperationException("原位置不存在。");
    }
    internal void Add(Farm farm, Vector2 tile)
    {
        if (Object is { } obj) farm.objects.Add(tile, obj);
        else farm.terrainFeatures.Add(tile, (TerrainFeature)Value);
        if (Tapper != null) farm.objects.Add(tile, Tapper);
    }

    // Roll back each dictionary component independently, including a half-moved tree/tapper.
    internal void RemoveOwned(Farm farm, Vector2 tile) => ForEachComponent((objects, value) =>
    {
        if (objects)
        {
            if (farm.objects.TryGetValue(tile, out var obj) && ReferenceEquals(obj, value)) farm.objects.Remove(tile);
        }
        else if (farm.terrainFeatures.TryGetValue(tile, out var terrain) && ReferenceEquals(terrain, value)) farm.terrainFeatures.Remove(tile);
    });

    internal void Restore(Farm farm, Vector2 tile) => ForEachComponent((objects, value) =>
    {
        object? current = objects ? farm.objects.TryGetValue(tile, out var obj) ? obj : null
            : farm.terrainFeatures.TryGetValue(tile, out var terrain) ? terrain : null;
        if (ReferenceEquals(current, value)) return;
        if (current != null) throw new InvalidOperationException("恢复位置被其他对象占用。");
        if (objects) farm.objects.Add(tile, (SObject)value);
        else farm.terrainFeatures.Add(tile, (TerrainFeature)value);
    });

    private void ForEachComponent(Action<bool, object> action)
    {
        var errors = new List<Exception>();
        try { action(ObjectLayer, Value); } catch (Exception error) { errors.Add(error); }
        if (Tapper != null)
            try { action(true, Tapper); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    internal void Refresh(Farm farm, Vector2 tile)
    {
        if (!IsAt(farm, tile)) throw new InvalidOperationException("物体位置不一致，不能刷新。");
        if (PlacedObject is { } obj)
        {
            obj.Location = farm;
            obj.TileLocation = tile;
        }
        else if (Dirt is { } dirt)
        {
            dirt.nearWaterForPaddy.Value = -1;
            dirt.crop?.updateDrawMath(tile);
            dirt.updateNeighbors();
        }
        // Flooring adjacency is updated by the terrain dictionary's add/remove callbacks.
        // Fence connections are calculated from the current object positions when drawn.
    }

    internal void DrawPreview(SpriteBatch b, Vector2 tile, Color tint)
    {
        if (IsTree)
        {
            TreePreview.Draw(b, this, tile, tint * 0.65f);
            return;
        }
        if (Dirt?.crop is { } crop)
        {
            crop.drawWithOffset(b, tile, tint * 0.65f, 0, new Vector2(32, 32));
            return;
        }
        string? id = Object?.QualifiedItemId ?? (Value as Flooring)?.GetData()?.ItemId;
        if (id == null) return;
        // Draw item data directly: calling object.draw can mutate animation state or
        // read its real location. A preview must never relocate the live instance.
        var data = ItemRegistry.GetDataOrErrorItem(id);
        Rectangle source = data.GetSourceRect();
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, tile * 64 + new Vector2(32, 64));
        b.Draw(data.GetTexture(), position, source, tint * 0.65f, 0,
            new Vector2(source.Width / 2f, source.Height), 4, SpriteEffects.None, 1);
    }
}
