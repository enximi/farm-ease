using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace FarmMenu;

internal static class NativeMenuIntegration
{
    internal const string TabName = "zzz.FarmEase";
    internal const int TabId = 984200;
    private static MenuController mod = null!;

    internal static void Install(MenuController owner)
    {
        mod = owner;
        var harmony = new Harmony(owner.Mod.ModManifest.UniqueID + ".Menu");
        // Patch the inner constructor so new GameMenu(customIndex), including resize, is valid.
        harmony.Patch(AccessTools.Constructor(typeof(GameMenu), new[] { typeof(bool) }),
            postfix: new HarmonyMethod(typeof(NativeMenuIntegration), nameof(AddTab)));
        harmony.Patch(AccessTools.Method(typeof(GameMenu), nameof(GameMenu.getTabNumberFromName), new[] { typeof(string) }),
            postfix: new HarmonyMethod(typeof(NativeMenuIntegration), nameof(ResolveTab)));
        harmony.Patch(AccessTools.Method(typeof(GameMenu), nameof(GameMenu.receiveGamePadButton), new[] { typeof(Buttons) }),
            prefix: new HarmonyMethod(typeof(NativeMenuIntegration), nameof(SwitchBoundaryTab)));
        harmony.Patch(AccessTools.Method(typeof(GameMenu), nameof(GameMenu.draw), new[] { typeof(SpriteBatch) }),
            postfix: new HarmonyMethod(typeof(NativeMenuIntegration), nameof(DrawTab)));
    }

    private static void AddTab(GameMenu __instance)
    {
        if (__instance.tabs.Any(t => t.name == TabName)) return;
        var previous = __instance.tabs[^1];
        var tab = new ClickableComponent(new Rectangle(previous.bounds.Right, previous.bounds.Y, 64, 64), TabName, "农场随心")
        {
            myID = TabId, leftNeighborID = previous.myID, downNeighborID = HubPage.FirstRowId,
            tryDefaultIfNoDownNeighborExists = true, fullyImmutable = true
        };
        previous.rightNeighborID = TabId;
        tab.rightNeighborID = __instance.tabs[0].myID;
        __instance.tabs[0].leftNeighborID = TabId;
        __instance.tabs.Add(tab);
        __instance.pages.Add(new HubPage(mod, __instance));
        __instance.GetCurrentPage().allClickableComponents.Add(tab);
    }

    private static void ResolveTab(GameMenu __instance, string name, ref int __result)
    {
        if (name == TabName) __result = __instance.tabs.FindIndex(t => t.name == TabName);
    }

    private static bool SwitchBoundaryTab(GameMenu __instance, Buttons button)
    {
        int last = __instance.pages.Count - 1;
        if (last < 1 || __instance.pages[last] is not HubPage) return true;
        int target;
        if (button == Buttons.LeftTrigger && __instance.currentTab == 0) target = last;
        else if (button == Buttons.RightTrigger && __instance.currentTab == last) target = 0;
        else if (button == Buttons.RightTrigger && __instance.currentTab >= GameMenu.numberOfTabs
            && __instance.currentTab + 1 == last) target = last;
        else return true;
        // Honor native close restrictions, including held items and forcePreventClose.
        if (__instance.readyToClose()) __instance.changeTab(target);
        return false;
    }

    private static void DrawTab(GameMenu __instance, SpriteBatch b)
    {
        if (__instance.invisible) return;
        var tab = __instance.tabs.Find(t => t.name == TabName);
        if (tab == null) return;
        int x = tab.bounds.X, y = tab.bounds.Y + (__instance.GetCurrentPage() is HubPage ? 8 : 0);
        // The skills tab provides the native empty tab frame (its portrait is drawn separately).
        b.Draw(Game1.mouseCursors, new Vector2(x, y), new Rectangle(16, 368, 16, 16), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        b.Draw(Game1.staminaRect, new Rectangle(x + 28, y + 24, 8, 24), new Color(67, 112, 48));
        b.Draw(Game1.staminaRect, new Rectangle(x + 16, y + 20, 16, 12), new Color(106, 165, 63));
        b.Draw(Game1.staminaRect, new Rectangle(x + 36, y + 16, 16, 12), new Color(139, 188, 76));
        b.Draw(Game1.staminaRect, new Rectangle(x + 20, y + 44, 28, 8), new Color(140, 86, 48));
        if (tab.containsPoint(Game1.getMouseX(), Game1.getMouseY()))
        {
            IClickableMenu.drawHoverText(b, tab.label, Game1.smallFont);
            if (!Game1.options.hardwareCursor) __instance.drawMouse(b);
        }
    }
}
