using Microsoft.Xna.Framework;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace GardenEase;

internal static class CampfireSounds
{
    // Ambient sounds are local to each screen, not network fields. Observe the
    // final dictionary changes on each peer, including swaps, undo and rollback.
    internal static void OnObjectsChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (e.Location is not Farm || !ReferenceEquals(e.Location, Game1.currentLocation)) return;
        var changed = new HashSet<Vector2>();
        foreach (var pair in e.Removed)
        {
            if (!ArrangeItem.IsCampfire(pair.Value)) continue;
            // Use the old dictionary key: the original instance has already moved.
            AmbientLocationSounds.removeSound(pair.Key);
            changed.Add(pair.Key);
        }
        foreach (var pair in e.Added)
            if (ArrangeItem.IsCampfire(pair.Value)) changed.Add(pair.Key);

        // Remove all old sounds before adding any new ones (overlapping batches
        // and swaps can reuse old positions). Never invoke performRemoveAction,
        // which would extinguish the original campfire.
        foreach (Vector2 tile in changed)
            if (e.Location.objects.TryGetValue(tile, out var obj)
                && obj is Torch { bigCraftable.Value: true, IsOn: true })
                AmbientLocationSounds.addSound(tile, AmbientLocationSounds.sound_cracklingFire);
    }
}
