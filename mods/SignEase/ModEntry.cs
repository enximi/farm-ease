using FarmMenu;
using StardewModdingAPI;

namespace SignEase;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
}

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private MenuController menu = null!;
    private SignUpdater updater = null!;
    private bool saving;

    public override void Entry(IModHelper helper)
    {
        updater = new(Monitor);
        Reload();
        menu = new(this, Reload, Register);
        helper.Events.GameLoop.UpdateTicked += (_, e) =>
        {
            if (e.IsMultipleOf(30) && Context.IsWorldReady && Context.IsMainPlayer && config.Enabled && !saving)
                updater.Update();
        };
        helper.Events.GameLoop.Saving += (_, _) => { if (Context.IsMainPlayer) saving = true; };
        helper.Events.GameLoop.Saved += (_, _) => { if (Context.IsMainPlayer) saving = false; };
        helper.Events.GameLoop.SaveLoaded += (_, _) => { if (Context.IsMainPlayer) { saving = false; updater.Reset(); } };
        helper.Events.GameLoop.DayStarted += (_, _) => { if (Context.IsMainPlayer) saving = false; };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { saving = false; updater.Reset(); };
        Monitor.Log("一目了然已就绪：物品牌放在箱子正上方一格，自动显示首件物品；多人由房主更新。", LogLevel.Info);
    }
    public override object GetApi() => menu.PublicApi;
    private void Register(IFarmMenuApi api)
    {
        api.RegisterSection("signs", "", "一目了然", "箱子后方的物品牌自动显示箱内首件物品。", 50);
        api.RegisterAction("signs", "signs.enabled",
            () => Context.IsMainPlayer ? "自动展示 · " + (config.Enabled ? "已开启" : "已关闭") : "自动展示 · 由房主控制",
            () => "告示牌放在箱子正上方一格。空箱清空图标；关闭后保留最后的牌面，可手动修改。",
            Toggle, () => Context.IsWorldReady && Context.IsMainPlayer, 0);
    }
    private void Toggle()
    {
        if (!Context.IsMainPlayer) return;
        var next = new ModConfig { Enabled = !config.Enabled };
        try
        {
            Helper.WriteConfig(next); config = next; updater.Reset();
            menu.Notify(config.Enabled ? "自动展示已开启。" : "自动展示已关闭，保留当前牌面。");
        }
        catch (Exception error) { Monitor.Log($"配置保存失败：{error}", LogLevel.Error); }
    }
    private void Reload()
    {
        try { config = Helper.ReadConfig<ModConfig>(); updater.Reset(); }
        catch (Exception error) { Monitor.Log($"配置读取失败，继续使用原设置：{error}", LogLevel.Warn); }
    }
}
