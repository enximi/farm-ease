using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace SignEase;

// Only the host writes native Sign fields. Clients need no new protocol or mod.
internal sealed class SignUpdater
{
    private sealed record Display(Item? Item, int Type, byte[] Appearance);
    private readonly Dictionary<Sign, Display> displays = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Type> reportedTypes = new();
    private readonly IMonitor monitor;
    internal SignUpdater(IMonitor monitor) => this.monitor = monitor;

    internal void Update()
    {
        var seen = new HashSet<Sign>(ReferenceEqualityComparer.Instance);
        Utility.ForEachLocation(location =>
        {
            foreach (var pair in location.objects.Pairs)
            {
                if (pair.Value is not Sign sign || sign.GetType() != typeof(Sign)) continue;
                if (!location.objects.TryGetValue(pair.Key + new Vector2(0, 1), out var below)
                    || below is not Chest chest || !Supported(chest)) continue;
                seen.Add(sign);
                // A take/craft operation can be in flight. Its completion will be
                // reflected on the next pass; this observer never requests a lock.
                if (chest.GetMutex().IsLocked() || chest.isTemporarilyInvisible || chest.localKickStartTile != null
                    || sign.isTemporarilyInvisible) continue;
                var first = chest.GetItemsForPlayer().FirstOrDefault(item => item != null && item.Stack > 0);
                try { Update(sign, first); }
                catch (Exception error)
                {
                    // A third-party item with an unsupported getOne/serializer
                    // must not stop other signs or flood the log every half second.
                    if (reportedTypes.Add(first?.GetType() ?? typeof(Sign)))
                        monitor.Log($"未能更新告示牌 ({location.NameOrUniqueName} {pair.Key})，保留原牌面：{error}", LogLevel.Warn);
                }
            }
            return true;
        }, includeInteriors: true, includeGenerated: false);
        foreach (var sign in displays.Keys.Where(sign => !seen.Contains(sign)).ToArray()) displays.Remove(sign);
    }

    private void Update(Sign sign, Item? first)
    {
        if (first == null)
        {
            if (sign.displayItem.Value != null) sign.displayItem.Value = null;
            if (sign.displayType.Value != 0) sign.displayType.Value = 0;
            displays.Remove(sign);
            return;
        }
        // Exactly like vanilla manual sign assignment: a one-item display copy,
        // never the live inventory reference. No chest item is removed or edited.
        var copy = first.getOne();
        if (ReferenceEquals(copy, first)) throw new InvalidOperationException("物品未返回独立的展示副本。");
        int type = copy switch
        {
            Hat => Sign.HAT,
            Ring => Sign.RING,
            Furniture => Sign.FURNITURE,
            SObject obj when obj.bigCraftable.Value => Sign.BIG_OBJECT,
            _ => Sign.OBJECT
        };
        // Serialize only the detached display copy, not a live NetRoot or source
        // inventory. This catches dye/preserved-item/tool metadata, not just IDs,
        // and avoids publishing a new sign field when the appearance is unchanged.
        byte[] appearance;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) copy.NetFields.WriteFull(writer);
            appearance = stream.ToArray();
        }
        if (displays.TryGetValue(sign, out var previous) && ReferenceEquals(sign.displayItem.Value, previous.Item)
            && previous.Item?.GetType() == copy.GetType() && previous.Item.QualifiedItemId == copy.QualifiedItemId
            && sign.displayType.Value == type && previous.Type == type && previous.Appearance.AsSpan().SequenceEqual(appearance)) return;
        sign.displayItem.Value = copy;
        sign.displayType.Value = type;
        displays[sign] = new(copy, type, appearance);
    }
    private static bool Supported(Chest chest) => chest.GetType() == typeof(Chest) && chest.playerChest.Value
        && !chest.fridge.Value && !chest.giftbox.Value && chest.GlobalInventoryId == null
        && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest or Chest.SpecialChestTypes.JunimoChest;
    internal void Reset() { displays.Clear(); reportedTypes.Clear(); }
}
