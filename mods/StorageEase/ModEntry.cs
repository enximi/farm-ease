using FarmMenu;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace StorageEase;

public sealed class ModEntry : Mod
{
    private ModConfig defaults = new();
    private readonly PerScreen<ModConfig?> configurations = new();
    internal ModConfig Config => configurations.Value ??= defaults.Copy();
    internal MenuController Menu { get; private set; } = null!;
    internal StorageNetwork Network { get; private set; } = null!;
    internal StorageAccess Access { get; private set; } = null!;
    internal StorageCrafting Crafting { get; private set; } = null!;
    internal StorageTabs Tabs { get; private set; } = null!;
    private bool ready;
    internal bool InRange => ready && Context.IsWorldReady && !Game1.eventUp && !Game1.isFestival()
        && !Game1.fadeToBlack && Game1.locationRequest == null && Game1.player.health > 0
        && (Config.Anywhere || StorageCatalog.InComfortArea());
    internal bool HasStorageScreen => Game1.activeClickableMenu is StorageBrowser || Access.Busy || Tabs.IsOpen || StorageCrafting.CurrentPage != null;
    public override void Entry(IModHelper helper)
    {
        Reload();
        Network = new(this); Access = new(this); Crafting = new(this); Tabs = new(this);
        Menu = new(this, Reload, Register);
        StoragePatches.Mod = this;
        var harmony = new Harmony(ModManifest.UniqueID);
        try { StoragePatches.Install(harmony); ready = true; }
        catch (Exception error) { harmony.UnpatchAll(ModManifest.UniqueID); Report(error); }
        helper.Events.GameLoop.UpdateTicked += (_, _) =>
        {
            if (!Context.IsWorldReady || !ready) return;
            try { Network.Tick(); Access.Tick(); Tabs.Tick(); }
            catch (Exception error) { Access.Release(); Report(error); }
        };
        helper.Events.Input.ButtonPressed += (_, e) => Tabs.OnButton(e);
        helper.Events.Display.RenderedActiveMenu += (_, e) => Tabs.Draw(e.SpriteBatch);
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) Access.Release(); };
        helper.Events.GameLoop.Saving += (_, _) => Access.Release();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            Access.Reset(); Tabs.Reset();
            if (Context.IsMainPlayer) configurations.ResetAllScreens(); else configurations.Value = null;
        };
        helper.ConsoleCommands.Add("storageease", "打开随取随用仓储页面。", (_, _) => Menu.Hub?.Open("storage"));
    }
    public override object GetApi() => Menu.PublicApi;
    private void Register(IFarmMenuApi api)
    {
        api.RegisterSection("storage", "", "随取随用", "随地找箱子、取放物品，制作时自动取用已允许的材料。", 40);
        api.RegisterAction("storage", "storage.browse", () => "打开仓储", () => "查看自己的箱子和他人共享箱子；搜索物品，分别设置远程访问、合成取材和共享。",
            OpenBrowser, () => ready, 10);
        api.RegisterAction("storage", "storage.mode", () => "范围：" + (Config.Anywhere ? "随行模式" : "舒适模式"),
            () => "随行：各处都可使用。舒适：仅在农场或农舍内使用。选择切换，自动保存。",
            () => Change(c => c.Anywhere = !c.Anywhere), () => ready, 20);
        api.RegisterAction("storage", "storage.craft", () => "制作自动取材：" + (Config.CraftFromStorage ? "开" : "关"),
            () => "适用于原版制作页，先用背包，不足时取用允许的箱子。成品放入背包。",
            () => Change(c => c.CraftFromStorage = !c.CraftFromStorage), () => ready, 30);
        api.RegisterAction("storage", "storage.cook", () => "烹饪自动取材：" + (Config.CookFromStorage ? "开" : "关"),
            () => "默认关闭；开启后在原版厨房或野炊工具的烹饪页面取材，不提供随地烹饪入口。",
            () => Change(c => c.CookFromStorage = !c.CookFromStorage), () => ready, 40);
    }
    internal void OpenBrowser()
    {
        if (!InRange) { Menu.Notify("请结束当前操作，或回到农场 / 农舍；随行模式可在其他地点使用。"); return; }
        Game1.activeClickableMenu?.exitThisMenu(false);
        Game1.activeClickableMenu = new StorageBrowser(this);
        Network.Refresh();
    }
    private void Change(Action<ModConfig> change)
    {
        var next = Config.Copy(); change(next);
        try { Helper.WriteConfig(next); defaults = next.Copy(); configurations.Value = next; Menu.Notify("随取随用设置已保存。"); }
        catch (Exception error) { Report(error); Menu.Notify("设置保存失败，继续使用原设置。"); }
    }
    private void Reload()
    {
        try { defaults = Helper.ReadConfig<ModConfig>(); configurations.Value = defaults.Copy(); }
        catch (Exception error) { Report(error); }
    }
    internal void Report(Exception error) => Monitor.Log(error.ToString(), LogLevel.Error);
}
