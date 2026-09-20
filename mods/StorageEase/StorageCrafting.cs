using HarmonyLib;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Objects;

namespace StorageEase;

internal sealed class StorageCrafting
{
    private readonly ModEntry mod;
    private readonly PerScreen<bool> executing = new(() => false);
    private sealed class Batch
    {
        internal readonly CraftingPage Page;
        internal readonly ClickableTextureComponent Component;
        internal int Count = 1;
        internal Batch(CraftingPage page, ClickableTextureComponent component) { Page = page; Component = component; }
    }
    private readonly PerScreen<Batch?> pending = new();
    internal StorageCrafting(ModEntry mod) => this.mod = mod;
    internal static CraftingPage? CurrentPage => Game1.activeClickableMenu as CraftingPage
        ?? (Game1.activeClickableMenu as GameMenu)?.GetCurrentPage() as CraftingPage;
    internal bool Enabled(CraftingPage page) => mod.InRange && (page.cooking ? mod.Config.CookFromStorage : mod.Config.CraftFromStorage);
    private IEnumerable<(BoxInfo Info, Chest Chest)> Candidates(CraftingPage page)
    {
        var seen = new HashSet<IInventory>(page._materialContainers ?? new());
        foreach (var info in mod.Network.Boxes.Where(box => box.Craft))
        {
            var chest = StorageCatalog.Resolve(info);
            if (chest == null || !StorageCatalog.Allowed(chest, Game1.player.UniqueMultiplayerID) || !StorageCatalog.Flag(chest, "craft", true)
                || !StorageCatalog.Available(chest) || (chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld())) continue;
            if (seen.Add(chest.GetItemsForPlayer())) yield return (info, chest);
        }
    }
    internal void AddContents(CraftingPage page, ref IList<Item>? contents)
    {
        if (!Enabled(page) || executing.Value) return;
        var combined = contents?.ToList() ?? new List<Item>();
        foreach (var (_, chest) in Candidates(page)) combined.AddRange(chest.GetItemsForPlayer());
        contents = combined;
    }
    internal bool BeforeCraft(CraftingPage page, ClickableTextureComponent component, bool playSound)
    {
        if (executing.Value || !Enabled(page)) return true;
        if (mod.Access.Busy || mod.Network.Loading)
        {
            if (mod.Access.Busy && pending.Value is { } batch && ReferenceEquals(batch.Page, page) && ReferenceEquals(batch.Component, component))
                batch.Count = Math.Min(25, batch.Count + 1);
            else if (playSound) mod.Menu.Notify("正在同步材料，请稍后再制作。");
            return false;
        }
        if (!page.pagesOfCraftingRecipes[page.currentCraftingPage].TryGetValue(component, out var recipe)) return false;
        if (page.heldItem != null) { mod.Menu.Notify("请先把手中的物品放入背包。"); return false; }
        var result = recipe.createItem();
        if (!Game1.player.couldInventoryAcceptThisItem(result)) { mod.Menu.Notify("背包空间不足，请先整理出位置。"); return false; }
        var original = page._materialContainers;
        var candidates = Candidates(page).ToArray();
        // Select only containers needed by this recipe. Simulate the native order
        // with per-stack remaining counts so category ingredients cannot double-count.
        var remaining = new Dictionary<Item, int>(ReferenceEqualityComparer.Instance);
        int Take(IEnumerable<Item> items, string ingredient, int amount)
        {
            foreach (var item in items.Reverse())
            {
                if (item == null || !CraftingRecipe.ItemMatchesForCrafting(item, ingredient)) continue;
                int count = remaining.TryGetValue(item, out int left) ? left : item.Stack;
                int used = Math.Min(count, amount); remaining[item] = count - used; amount -= used;
                if (amount <= 0) break;
            }
            return amount;
        }
        var required = new HashSet<Chest>();
        foreach (var ingredient in recipe.recipeList)
        {
            int need = Take(Game1.player.Items, ingredient.Key, ingredient.Value);
            foreach (var inventory in original ?? new List<IInventory>()) need = Take(inventory, ingredient.Key, need);
            foreach (var (_, chest) in candidates)
            {
                if (need <= 0) break;
                int before = need;
                need = Take(chest.GetItemsForPlayer(), ingredient.Key, need);
                if (need < before) required.Add(chest);
            }
            if (need > 0) { mod.Menu.Notify("材料已经变化或箱子正在使用，请稍后重试。"); return false; }
        }
        // Seasoning is optional, but a remote stack must be locked too before
        // the native cooking method upgrades quality and consumes it.
        if (page.cooking && result.Quality == 0)
        {
            int seasoning = Take(Game1.player.Items, "917", 1);
            foreach (var inventory in original ?? new List<IInventory>()) seasoning = Take(inventory, "917", seasoning);
            foreach (var (_, chest) in candidates)
            {
                if (seasoning <= 0) break;
                int before = seasoning;
                seasoning = Take(chest.GetItemsForPlayer(), "917", seasoning);
                if (seasoning < before) required.Add(chest);
            }
        }
        var selected = candidates.Where(c => required.Contains(c.Chest)).ToArray();
        var batchRequest = pending.Value = new Batch(page, component);
        bool Valid() => ReferenceEquals(CurrentPage, page) && Enabled(page) && page.heldItem == null
            && page.pagesOfCraftingRecipes[page.currentCraftingPage].TryGetValue(component, out var currentRecipe) && ReferenceEquals(currentRecipe, recipe);
        void Craft()
        {
            try
            {
                if (!Valid() || !Game1.player.couldInventoryAcceptThisItem(recipe.createItem())) return;
                var extras = (original ?? new List<IInventory>()).ToList();
                foreach (var (info, chest) in selected)
                {
                    if (!ReferenceEquals(StorageCatalog.Resolve(info), chest) || !chest.GetMutex().IsLockHeld()
                        || !StorageCatalog.Allowed(chest, Game1.player.UniqueMultiplayerID) || !StorageCatalog.Flag(chest, "craft", true)) return;
                    if (!extras.Contains(chest.GetItemsForPlayer())) extras.Add(chest.GetItemsForPlayer());
                }
                page._materialContainers = extras;
                for (int iteration = 0; iteration < batchRequest.Count; iteration++)
                {
                    if (!Valid() || !Game1.player.couldInventoryAcceptThisItem(recipe.createItem())) break;
                    remaining.Clear();
                    foreach (var ingredient in recipe.recipeList)
                    {
                        int need = Take(Game1.player.Items, ingredient.Key, ingredient.Value);
                        foreach (var inventory in extras) need = Take(inventory, ingredient.Key, need);
                        if (need > 0) { if (iteration == 0) mod.Menu.Notify("材料已不足，这次没有制作。"); return; }
                    }
                    executing.Value = true;
                    AccessTools.Method(typeof(CraftingPage), "clickCraftingRecipe").Invoke(page, new object[] { component, playSound && iteration == 0 });
                    if (page.heldItem is { } made && Game1.player.couldInventoryAcceptThisItem(made)
                        && Game1.player.addItemToInventoryBool(made)) page.heldItem = null;
                }
            }
            finally { pending.Value = null; executing.Value = false; page._materialContainers = original; mod.Access.Release(); }
        }
        if (selected.Length == 0) Craft();
        else mod.Access.Acquire(selected.Select(c => c.Info).ToList(), true, Valid, Craft);
        return false;
    }
}
