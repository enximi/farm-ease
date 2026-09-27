using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;

namespace StorageEase;

internal static class StoragePatches
{
    internal static ModEntry Mod = null!;
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Chest), nameof(Chest.checkForAction)),
            prefix: new HarmonyMethod(typeof(StoragePatches), nameof(PhysicalOpen)));
        harmony.Patch(AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveLeftClick)),
            prefix: new HarmonyMethod(typeof(StoragePatches), nameof(LeftClick)));
        harmony.Patch(AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveRightClick)),
            prefix: new HarmonyMethod(typeof(StoragePatches), nameof(RightClick)));
        harmony.Patch(AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.FillOutStacks)),
            prefix: new HarmonyMethod(typeof(StoragePatches), nameof(FillStacks)));
        harmony.Patch(AccessTools.Method(typeof(Chest), nameof(Chest.ShowMenu)),
            postfix: new HarmonyMethod(typeof(StoragePatches), nameof(ChestOpened)));
        harmony.Patch(AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.isAlwaysActiveLocation)),
            postfix: new HarmonyMethod(typeof(StoragePatches), nameof(AlwaysActive)));
        harmony.Patch(AccessTools.Method(typeof(Multiplayer), "broadcastLocationBytes"),
            postfix: new HarmonyMethod(typeof(StoragePatches), nameof(Forward)));
        harmony.Patch(AccessTools.Method(typeof(NetMutex), nameof(NetMutex.Update), new[] { typeof(FarmerCollection) }),
            prefix: new HarmonyMethod(typeof(StoragePatches), nameof(UpdateMutex)));
        harmony.Patch(AccessTools.Method(typeof(CraftingPage), "getContainerContents"),
            postfix: new HarmonyMethod(typeof(StoragePatches), nameof(Contents)));
        harmony.Patch(AccessTools.Method(typeof(CraftingPage), "clickCraftingRecipe"),
            prefix: new HarmonyMethod(typeof(StoragePatches), nameof(Craft)));
    }
    public static bool PhysicalOpen(Chest __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
        => Mod.Access.PhysicalOpen(__instance, who, justCheckingForActivity, ref __result);
    public static bool LeftClick(ItemGrabMenu __instance, int x, int y, bool playSound)
        => Mod.Access.Input(__instance, () => __instance.receiveLeftClick(x, y, playSound), x, y);
    public static bool RightClick(ItemGrabMenu __instance, int x, int y, bool playSound)
        => Mod.Access.Input(__instance, () => __instance.receiveRightClick(x, y, playSound), x, y);
    public static bool FillStacks(ItemGrabMenu __instance)
        => Mod.Access.Input(__instance, __instance.FillOutStacks);
    public static void ChestOpened() => Mod.Access.TrackMenu();
    public static void AlwaysActive(GameLocation location, ref bool __result)
    {
        if (Context.IsWorldReady && !__result && Mod.Network.Subscribed(location)) __result = true;
    }
    public static void Forward(GameLocation loc, byte messageType, byte[] bytes)
    {
        if (Context.IsWorldReady) Mod.Network.Forward(loc, messageType, bytes);
    }
    public static void UpdateMutex(NetMutex __instance, ref FarmerCollection farmers)
    {
        if (Context.IsWorldReady && Context.IsMainPlayer && Mod.Network.IsRemoteMutex(__instance)) farmers = Game1.getOnlineFarmers();
    }
    public static void Contents(CraftingPage __instance, ref IList<Item>? __result)
        => Mod.Crafting.AddContents(__instance, ref __result);
    public static bool Craft(CraftingPage __instance, ClickableTextureComponent c, bool playSound)
        => Mod.Crafting.BeforeCraft(__instance, c, playSound);
}
