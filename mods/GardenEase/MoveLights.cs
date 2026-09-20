using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Extensions;
using SObject = StardewValley.Object;

namespace GardenEase;

// Lighting is indexed by position separately from the placed objects.
internal sealed class MoveLights
{
    private sealed record Entry(LightSource Light, string OldId, string NewId, Vector2 OldPosition, Vector2 Offset, bool Registered);
    private readonly Farm farm;
    private readonly List<Entry> entries = new();
    internal MoveLights(Farm farm) => this.farm = farm;
    internal void Capture(ArrangeItem item, Vector2 from, Vector2 to)
    {
        void CaptureObject(SObject obj)
        {
            if (obj.lightSource is not { } light) return;
            entries.Add(new(light, light.Id, obj.Location == null ? light.Id : obj.GenerateLightSourceId(to), light.position.Value,
                (to - from) * 64, farm.hasLightSource(light.Id)));
        }
        if (item.PlacedObject is not { } placed) return;
        CaptureObject(placed);
        if (placed is Fence && placed.heldObject.Value is { } torch) CaptureObject(torch);
    }
    internal void Detach()
    {
        foreach (var entry in entries) farm.removeLightSource(entry.Light.Id);
    }
    internal void Apply(bool restore)
    {
        Detach();
        foreach (var entry in entries)
        {
            entry.Light.Id = restore ? entry.OldId : entry.NewId;
            entry.Light.position.Value = entry.OldPosition + (restore ? Vector2.Zero : entry.Offset);
            if (entry.Registered) farm.sharedLights.AddLight(entry.Light.Clone());
        }
    }
}
