using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Objects;

namespace StorageEase;

// Read the existing inventory; never sort it or create an item to render a label.
internal readonly record struct StoragePreview(string Name, string IconId)
{
    internal static StoragePreview From(Chest chest)
    {
        var first = chest.GetItemsForPlayer().FirstOrDefault(item => item != null && item.Stack > 0);
        return first == null ? new("空箱子", "") : new(first.DisplayName, first.QualifiedItemId);
    }
    internal static StoragePreview From(BoxInfo info)
        => StorageCatalog.Resolve(info) is { } chest ? From(chest) : new(info.Name, info.IconId);

    internal void DrawIcon(SpriteBatch b, Rectangle bounds, bool muted = false)
    {
        var data = ItemRegistry.GetDataOrErrorItem(IconId.Length == 0 ? "(BC)130" : IconId);
        var source = data.GetSourceRect();
        float scale = Math.Min(bounds.Width / (float)source.Width, bounds.Height / (float)source.Height);
        b.Draw(data.GetTexture(), new Vector2(bounds.Center.X, bounds.Center.Y), source,
            Color.White * (muted ? 0.45f : IconId.Length == 0 ? 0.6f : 1f), 0f,
            new Vector2(source.Width / 2f, source.Height / 2f), scale, SpriteEffects.None, 0.9f);
    }
}
