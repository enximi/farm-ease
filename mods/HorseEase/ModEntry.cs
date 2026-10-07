using FarmMenu;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;

namespace HorseEase;

public sealed class ModConfig
{
    public SButton KeyboardSummonButton { get; set; } = SButton.F10;
    public SButton ControllerSummonButton { get; set; } = SButton.None;
}

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private MenuController menu = null!;
    private readonly PerScreen<long> lastRequest = new(() => 0);

    public override void Entry(IModHelper helper)
    {
        Reload();
        menu = new(this, Reload, api => api.RegisterAction("", "horse.summon", () => "闻哨而来",
            () => $"将自己的马召到身边，无需携带马笛；室外使用，快捷键 {Ui.Button(config.KeyboardSummonButton)}。",
            Summon, () => true, 70));
        helper.Events.Input.ButtonPressed += (sender, e) =>
        {
            if (e.Button == SButton.None || (e.Button != config.KeyboardSummonButton && e.Button != config.ControllerSummonButton)
                || e.Button == menu.Settings.MenuButton || e.Button == menu.Settings.KeyboardMenuButton
                || helper.Input.IsSuppressed(e.Button) || !menu.CanUse(out _, allowMounted: true)) return;
            helper.Input.Suppress(e.Button);
            Summon();
        };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => lastRequest.ResetAllScreens();
        helper.ConsoleCommands.Add("horseease", "闻哨而来：呼唤自己的马。", (_, _) => Summon());
    }

    public override object GetApi() => menu.PublicApi;

    private void Summon()
    {
        if (!menu.CanUse(out string reason, true, allowMounted: true)) { menu.Notify(reason); return; }
        var player = Game1.player;
        if (player.isRidingHorse()) { menu.Notify("你已经骑在马上了。"); return; }
        if (player.IsSitting() || player.passedOut || Game1.newDay || player.timeWentToBed.Value != 0)
        { menu.Notify("请先结束休息或过夜，再呼唤坐骑。"); return; }
        // 与原版马笛共用限制；不生成新马，不改变归属，也不抢走其他玩家正在骑的马。
        var restrictions = Utility.GetHorseWarpRestrictionsForFarmer(player);
        if (restrictions != Utility.HorseWarpRestrictions.None)
        { menu.Notify(Utility.GetHorseWarpErrorMessage(restrictions)); return; }
        var nearby = player.currentLocation.characters.OfType<Horse>().FirstOrDefault(horse => horse.ownerId.Value == player.UniqueMultiplayerID
            && Math.Abs(horse.Tile.X - player.Tile.X) <= 1 && Math.Abs(horse.Tile.Y - player.Tile.Y) <= 1);
        if (nearby != null) { menu.Notify("你的马已经在身边了。"); return; }
        if (Environment.TickCount64 - lastRequest.Value < 1500) { menu.Notify("已经呼唤过了，请稍候。"); return; }
        try
        {
            Game1.activeClickableMenu?.exitThisMenu(false);
            lastRequest.Value = Environment.TickCount64;
            Game1.playSound("horse_flute");
            // 原生团队网络事件由房主复核并搬运原马；客机无需直接修改地图上的角色集合。
            player.team.requestHorseWarpEvent.Fire(player.UniqueMultiplayerID);
            menu.Notify("已呼唤坐骑。");
        }
        catch (Exception error)
        {
            Monitor.Log($"呼唤坐骑未完成：{error}", LogLevel.Error);
            menu.Notify("呼唤未完成，请查看 SMAPI 日志。");
        }
    }

    private void Reload()
    {
        try { config = Helper.ReadConfig<ModConfig>(); }
        catch (Exception error) { Monitor.Log($"配置读取失败，继续使用原设置：{error}", LogLevel.Warn); }
    }
}
