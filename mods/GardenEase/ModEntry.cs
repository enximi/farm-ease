using FarmMenu;
using StardewModdingAPI;
using StardewValley;

namespace GardenEase;

public sealed class ModEntry : Mod
{
    internal MenuController Menu { get; private set; } = null!;
    internal GardenNetwork Network { get; private set; } = null!;

    public override void Entry(IModHelper helper)
    {
        Network = new(this);
        helper.Events.World.ObjectListChanged += CampfireSounds.OnObjectsChanged;
        helper.Events.World.TerrainFeatureListChanged += LayoutDisplay.OnTerrainChanged;
        helper.Events.World.ObjectListChanged += LayoutDisplay.OnObjectsChanged;
        Menu = new(this, () => { }, api =>
        {
            api.RegisterAction("", "garden.arrange", () => "田园巧整",
                () => ArrangeLocation.Supports(Game1.currentLocation)
                    ? Context.IsMultiplayer ? "可多人同时整理不同格子，各自撤销；时间继续流逝。" : "搬移或交换作物、牧草、树木、道路与设施。支持撤销，整理期间时间暂停。"
                    : "请先进入农场室外或温室，再开始整理。",
                OpenArrange, () => ArrangeLocation.Supports(Game1.currentLocation), 20);
            api.RegisterAction("", "garden.pasture", () => "牧草疏植",
                () => "一键按棋盘格疏植现有牧草，优先鸡舍、畜棚周边；可整批撤销。",
                () => OpenArrange(true), () => Game1.currentLocation is Farm, 21);
        });
        helper.Events.Display.RenderedWorld += (_, e) => { if (Game1.activeClickableMenu is ArrangeMenu menu) menu.DrawWorld(e.SpriteBatch); };
        helper.Events.Display.MenuChanged += (_, e) =>
        {
            if (e.OldMenu is ArrangeMenu menu && !ReferenceEquals(e.OldMenu, e.NewMenu)) menu.RestoreCamera();
        };
        helper.Events.Input.ButtonPressed += (_, e) =>
        {
            if (Game1.activeClickableMenu is ArrangeMenu menu && e.Button is SButton.B or SButton.ControllerY)
            { helper.Input.Suppress(e.Button); menu.ToggleBatch(); }
        };
        helper.Events.Input.CursorMoved += (_, e) =>
        { if (Game1.activeClickableMenu is ArrangeMenu menu) menu.MoveMouse(e); };
        helper.ConsoleCommands.Add("gardenease", "直接进入田园巧整整理模式。", (_, _) => OpenArrange());
    }
    public override object GetApi() => Menu.PublicApi;

    private void OpenArrange() => OpenArrange(false);
    private void OpenArrange(bool spreadPasture)
    {
        if (!Menu.CanUse(out string reason, true)) { Menu.Notify(reason); return; }
        if (Network.UnavailableReason() is string unavailable) { Menu.Notify(unavailable); return; }
        var location = Game1.currentLocation;
        if (!ArrangeLocation.Supports(location)) { Menu.Notify("请先进入农场室外或温室，再开始整理。"); return; }
        if (spreadPasture && location is not Farm) { Menu.Notify("牧草疏植仅在农场室外使用。"); return; }
        Game1.activeClickableMenu?.exitThisMenu(false);
        Game1.activeClickableMenu = new ArrangeMenu(this, location, spreadPasture);
    }
    internal void Report(Exception error) => Monitor.Log(error.ToString(), LogLevel.Error);
}
