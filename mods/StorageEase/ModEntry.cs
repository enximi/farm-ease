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
    internal StorageNavigation Navigation { get; private set; } = null!;
    private bool ready;
    internal bool InRange => ready && Context.IsWorldReady && !Game1.eventUp && !Game1.isFestival()
        && !Game1.fadeToBlack && Game1.locationRequest == null && Game1.player.health > 0
        && (Config.Anywhere || StorageCatalog.InComfortArea());
    internal bool HasStorageScreen => Navigation.Active || Game1.activeClickableMenu is StorageBrowser || Access.Busy || Tabs.IsOpen || StorageCrafting.CurrentPage != null;
    public override void Entry(IModHelper helper)
    {
        Reload();
        Network = new(this); Access = new(this); Crafting = new(this); Tabs = new(this);
        Navigation = new(this);
        Menu = new(this, Reload, Register);
        StoragePatches.Mod = this;
        var harmony = new Harmony(ModManifest.UniqueID);
        try { StoragePatches.Install(harmony); ready = true; }
        catch (Exception error) { harmony.UnpatchAll(ModManifest.UniqueID); Report(error); }
        helper.Events.GameLoop.UpdateTicked += (_, _) =>
        {
            if (!Context.IsWorldReady || !ready) return;
            try { Access.Tick(); Network.Tick(); Tabs.Tick(); Navigation.Tick(); }
            catch (Exception error) { Access.Release(); Report(error); }
        };
        helper.Events.Input.ButtonPressed += (_, e) =>
        {
            Tabs.OnButton(e);
            if (e.Button != SButton.None && e.Button == Config.KeyboardOpenButton
                && e.Button != Menu.Settings.MenuButton && e.Button != Menu.Settings.KeyboardMenuButton
                && !helper.Input.IsSuppressed(e.Button) && Navigation.CanQuickOpen())
            { helper.Input.Suppress(e.Button); Navigation.OpenLast(false); }
        };
        helper.Events.Display.RenderedActiveMenu += (_, e) => Tabs.Draw(e.SpriteBatch);
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) { Navigation.CancelReturn(); Access.Release(); } };
        helper.Events.GameLoop.Saving += (_, _) => { Navigation.CancelReturn(); Access.Release(); };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            Access.Reset(); Tabs.Reset(); Navigation.Reset();
            if (Context.IsMainPlayer) configurations.ResetAllScreens(); else configurations.Value = null;
        };
        helper.ConsoleCommands.Add("storageease", "打开上次使用的箱子。", (_, _) => Navigation.OpenLast(false));
    }
    public override object GetApi() => Menu.PublicApi;
    private void Register(IFarmMenuApi api)
    {
        api.RegisterAction("", "storage.open", () => "随取随用",
            () => "直接打开上次使用的箱子；LT / RT 换箱，全部箱子入口可搜索和管理。",
            () => Navigation.OpenLast(true), () => ready, 40);
        Menu.RegisterMenuHold("storage.open", () => "快捷开箱", () => Navigation.OpenLast(false),
            () => Config.EnableMenuHold && Navigation.CanQuickOpen(), () => Config.MenuHoldMilliseconds);
    }
    internal void Change(Action<ModConfig> change)
    {
        var next = Config.Copy(); change(next); next.Normalize();
        try { Helper.WriteConfig(next); defaults = next.Copy(); configurations.Value = next; Menu.Notify("随取随用设置已保存。"); }
        catch (Exception error) { Report(error); Menu.Notify("设置保存失败，继续使用原设置。"); }
    }
    private void Reload()
    {
        try { defaults = Helper.ReadConfig<ModConfig>(); defaults.Normalize(); configurations.Value = defaults.Copy(); }
        catch (Exception error) { Report(error); }
    }
    internal void Report(Exception error) => Monitor.Log(error.ToString(), LogLevel.Error);
}
