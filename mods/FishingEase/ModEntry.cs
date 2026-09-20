using HarmonyLib;
using FarmMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace FishingEase;

public sealed class ModEntry : Mod
{
    private ModConfig defaults = new();
    private readonly PerScreen<ModConfig?> configurations = new();
    private ModConfig config { get => configurations.Value ??= defaults.Snapshot(); set => configurations.Value = value; }
    private bool patchesReady;
    private MenuController menu = null!;

    public override void Entry(IModHelper helper)
    {
        LoadConfig();
        menu = new(this, () => LoadConfig(), RegisterMenu);
        FishingPatches.ReadConfig = () => config;
        var harmony = new Harmony(ModManifest.UniqueID);
        try
        {
            var constructor = AccessTools.Constructor(typeof(BobberBar), new[]
            {
                typeof(string), typeof(float), typeof(bool), typeof(List<string>),
                typeof(string), typeof(bool), typeof(string), typeof(bool)
            }) ?? throw new MissingMethodException("未找到预期的 BobberBar 构造函数。");
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(FishingPatches), nameof(FishingPatches.AfterConstruct)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(BobberBar), nameof(BobberBar.update), new[] { typeof(GameTime) }),
                transpiler: new HarmonyMethod(typeof(FishingPatches), nameof(FishingPatches.UpdateTranspiler)));
            patchesReady = true;
            Monitor.Log("从容钓鱼已就绪：保留追鱼操作，扩大容错。输入 fishingease 查看配置。", LogLevel.Info);
        }
        catch (Exception error)
        {
            harmony.UnpatchAll(ModManifest.UniqueID);
            Monitor.Log($"钓鱼辅助未启用，已撤回本 Mod 的补丁。\n{error}", LogLevel.Error);
        }
        helper.ConsoleCommands.Add("fishingease", "钓鱼辅助：status / reload / preset light|normal|strong|off。配置从下一条鱼开始生效。", OnCommand);
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            if (Context.IsMainPlayer) configurations.ResetAllScreens();
            else configurations.Value = null;
        };
    }

    public override object GetApi() => menu.PublicApi;

    private void RegisterMenu(IFarmMenuApi api)
    {
        api.RegisterSection("fishing", "", "从容钓鱼", "调整辅助强度，保留追鱼操作与升级成长。", 30);
        AddPreset("off", "关闭辅助", "恢复原版钓鱼。从下一条鱼开始生效。", 0);
        AddPreset("light", "轻度辅助", "绿条加长 10%，宽限 0.15 秒，进度损失为原版的 80%。", 10);
        AddPreset("normal", "默认辅助", "绿条加长 20%，宽限 0.3 秒，进度损失为原版的 60%。", 20);
        AddPreset("strong", "大幅辅助", "绿条加长 35%，宽限 0.5 秒，进度损失为原版的 35%。", 30);

        void AddPreset(string key, string title, string description, int order)
            => api.RegisterAction("fishing", "fishing." + key,
                () => title + (IsPreset(key) ? " · 当前" : ""),
                () => !patchesReady ? "钓鱼补丁未能加载，请查看 SMAPI 日志。" : description + " 调整后自动保存。",
                () => { if (SavePreset(ModConfig.Preset(key)!)) menu.Notify("钓鱼辅助已保存，从下一条鱼生效。"); },
                () => patchesReady, order);
    }

    private bool IsPreset(string key)
    {
        ModConfig expected = ModConfig.Preset(key)!;
        if (!config.Enabled || !expected.Enabled) return config.Enabled == expected.Enabled;
        return config.BarSizeMultiplier == expected.BarSizeMultiplier
            && config.GracePeriodMilliseconds == expected.GracePeriodMilliseconds
            && config.GraceResetMilliseconds == expected.GraceResetMilliseconds
            && config.ProgressLossMultiplier == expected.ProgressLossMultiplier
            && config.BottomBounceMultiplier == expected.BottomBounceMultiplier;
    }

    private bool SavePreset(ModConfig next)
    {
        try { Helper.WriteConfig(next); defaults = next.Snapshot(); config = next; return true; }
        catch (Exception error) { Monitor.Log($"保存失败，继续使用原配置：{error.Message}", LogLevel.Error); return false; }
    }

    private bool LoadConfig()
    {
        try
        {
            ModConfig next = Helper.ReadConfig<ModConfig>();
            if (next.Normalize())
                Monitor.Log("部分钓鱼配置超出范围，已在内存中限制；可用 fishingease status 查看实际值。", LogLevel.Warn);
            config = next;
            defaults = next.Snapshot();
            return true;
        }
        catch (Exception error)
        {
            Monitor.Log($"配置读取失败，继续使用上一次有效设置（首次启动使用默认值）。原文件保留。\n{error.Message}", LogLevel.Warn);
            return false;
        }
    }

    private void OnCommand(string command, string[] args)
    {
        string action = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        if (action == "reload")
        {
            if (LoadConfig()) Monitor.Log("配置读取完成；从下一条鱼开始生效。", LogLevel.Info);
        }
        else if (action == "preset")
        {
            ModConfig? next = args.Length == 2 ? ModConfig.Preset(args[1]) : null;
            if (next == null)
            {
                Monitor.Log("用法：fishingease preset light|normal|strong|off", LogLevel.Info);
                return;
            }
            if (!SavePreset(next)) return;
            Monitor.Log("已保存预设，从下一条鱼开始生效。", LogLevel.Info);
        }
        else if (action != "status")
        {
            Monitor.Log("用法：fishingease status / reload / preset light|normal|strong|off", LogLevel.Info);
            return;
        }

        Monitor.Log($"补丁就绪={patchesReady}，辅助启用={config.Enabled}，绿条×{config.BarSizeMultiplier:0.##}，宽限={config.GracePeriodMilliseconds}ms，稳定恢复={config.GraceResetMilliseconds}ms，进度损失×{config.ProgressLossMultiplier:0.##}，触底反弹×{config.BottomBounceMultiplier:0.##}", LogLevel.Info);
        if (Game1.activeClickableMenu is BobberBar menu)
        {
            FishingSession? session = FishingPatches.GetSession(menu);
            Monitor.Log($"当前鱼：{menu.whichFish}，绿条高度={menu.bobberBarHeight}，位置={menu.bobberBarPos:0.##}，速度={menu.bobberBarSpeed:0.##}，进度={menu.distanceFromCatching:0.0000}，框住={menu.bobberInBar}，完美={menu.perfect}，宽限剩余={session?.GraceRemainingMilliseconds:0}ms，当前损失倍率={session?.LossMultiplier:0.###}", LogLevel.Info);
        }
    }
}
