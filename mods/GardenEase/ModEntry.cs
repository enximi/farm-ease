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
        Menu = new(this, () => { }, api =>
        {
            api.RegisterAction("", "garden.arrange", () => "田园巧整",
                () => Game1.currentLocation is Farm
                    ? Context.IsMultiplayer ? "可多人同时整理不同格子，各自撤销；时间继续流逝。" : "搬移或交换作物、牧草、树木、道路与设施。支持撤销，整理期间时间暂停。"
                    : "请先回到农场室外，再开始整理。",
                OpenArrange, () => Game1.currentLocation is Farm, 20);
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

    private void OpenArrange()
    {
        if (!Menu.CanUse(out string reason, true)) { Menu.Notify(reason); return; }
        if (Network.UnavailableReason() is string unavailable) { Menu.Notify(unavailable); return; }
        if (Game1.currentLocation is not Farm farm) { Menu.Notify("请先回到农场室外，再开始整理。"); return; }
        Game1.activeClickableMenu?.exitThisMenu(false);
        Game1.activeClickableMenu = new ArrangeMenu(this, farm);
    }
    internal void Report(Exception error) => Monitor.Log(error.ToString(), LogLevel.Error);
}
