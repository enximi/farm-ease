using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace GardenEase;

// Render the game's stage sprites without relocating or updating a live tree.
internal static class TreePreview
{
    internal static void Draw(SpriteBatch b, ArrangeItem item, Vector2 tile, Color tint)
    {
        Vector2 anchor = Game1.GlobalToLocal(Game1.viewport, tile * 64 + new Vector2(32, 64));
        void Sprite(Texture2D texture, Rectangle rect, Vector2 origin, bool flipped, Vector2 offset = default)
            => b.Draw(texture, anchor + offset, rect, tint, 0, origin, 4,
                flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 1);
        if (item.Value is Tree tree)
        {
            Texture2D texture = tree.texture.Value;
            if (texture == null) return;
            if (tree.growthStage.Value < 5)
            {
                Rectangle rect = tree.growthStage.Value switch
                {
                    0 => new(32, 128, 16, 16),
                    1 => new(0, 128, 16, 16),
                    2 => new(16, 128, 16, 16),
                    _ => new(0, 96, 16, 32)
                };
                Sprite(texture, rect, new(8, rect.Height), tree.flipped.Value);
            }
            else
            {
                int topX = 0;
                if (Tree.TryGetData(tree.treeType.Value, out var data)
                    && ((data.UseAlternateSpriteWhenSeedReady && tree.hasSeed.Value)
                        || (data.UseAlternateSpriteWhenNotShaken && !tree.wasShakenToday.Value))) topX = 48;
                if (tree.hasMoss.Value) topX = 96;
                if (!tree.stump.Value) Sprite(texture, new(topX, 0, 48, 96), new(24, 96), tree.flipped.Value);
                Sprite(texture, new(tree.hasMoss.Value ? 128 : 32, 96, 16, 32), new(8, 32), tree.flipped.Value);
            }
        }
        else if (item.Value is FruitTree fruit)
        {
            int row = fruit.GetSpriteRowNumber() * 80;
            if (fruit.growthStage.Value < 4)
                Sprite(fruit.texture, new(Math.Clamp(fruit.growthStage.Value, 0, 3) * 48, row, 48, 80), new(24, 80), fruit.flipped.Value, new(0, -16));
            else
            {
                if (!fruit.stump.Value)
                {
                    int x = (12 + (int)fruit.GetCosmeticSeason() * 3) * 16;
                    Sprite(fruit.texture, new(x, row + 64, 48, 16), new(24, 16), fruit.flipped.Value);
                    Sprite(fruit.texture, new(x, row, 48, 64), new(24, 80), fruit.flipped.Value);
                }
                Sprite(fruit.texture, new(384, row + 48, 48, 32), new(24, 32), fruit.flipped.Value);
                if (!fruit.stump.Value)
                    for (int i = 0; i < Math.Min(3, fruit.fruit.Count); i++)
                    {
                        var fruitData = ItemRegistry.GetDataOrErrorItem(fruit.struckByLightningCountdown.Value > 0 ? "(O)382" : fruit.fruit[i].QualifiedItemId);
                        Sprite(fruitData.GetTexture(), fruitData.GetSourceRect(), Vector2.Zero, false,
                            i == 0 ? new(-80, -256) : i == 1 ? new(0, -304) : new(-24, -216));
                    }
            }
        }
        if (item.Tapper is { } tapper)
        {
            var data = ItemRegistry.GetDataOrErrorItem(tapper.QualifiedItemId);
            Rectangle rect = data.GetSourceRect();
            Sprite(data.GetTexture(), rect, new(rect.Width / 2f, rect.Height), false);
        }
    }
}
