using FarmMenu;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley.Characters;

namespace RidingEase;

public sealed class ModConfig
{
    public bool DisableHorseRumble { get; set; } = true;
}

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private MenuController menu = null!;
    private bool patchesReady;

    public override void Entry(IModHelper helper)
    {
        Reload();
        menu = new(this, Reload, Register);
        RidingPatches.ShouldSuppress = () => config.DisableHorseRumble;
        var harmony = new Harmony(ModManifest.UniqueID);
        try
        {
            var method = AccessTools.DeclaredMethod(typeof(Horse), nameof(Horse.PerformDefaultHorseFootstep), new[] { typeof(string) })
                ?? throw new MissingMethodException("未找到原版马蹄落地方法。");
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(RidingPatches), nameof(RidingPatches.FootstepTranspiler)));
            patchesReady = true;
            Monitor.Log("悠然骑行已就绪：可在农场随心菜单关闭骑马震动，保留马蹄声与其他震动。", LogLevel.Info);
        }
        catch (Exception error)
        {
            harmony.UnpatchAll(ModManifest.UniqueID);
            Monitor.Log($"骑马震动补丁未启用，已撤回本 Mod 的功能补丁。\n{error}", LogLevel.Error);
        }
    }

    public override object GetApi() => menu.PublicApi;

    private void Register(IFarmMenuApi api)
    {
        api.RegisterSection("riding", "", "悠然骑行", "关闭马蹄落地的手柄震动，轻松骑行。", 60);
        api.RegisterAction("riding", "riding.rumble",
            () => "关闭骑马震动 · " + (patchesReady ? config.DisableHorseRumble ? "已开启" : "已关闭" : "未生效"),
            () => patchesReady ? "保留马蹄声和钓鱼、受伤等其他震动。本机分屏共用此设置，修改后自动保存。"
                : "骑马震动补丁未能加载，请查看 SMAPI 日志。",
            Toggle, () => patchesReady, 0);
    }

    private void Toggle()
    {
        var next = new ModConfig { DisableHorseRumble = !config.DisableHorseRumble };
        try
        {
            Helper.WriteConfig(next);
            config = next;
            menu.Notify(config.DisableHorseRumble ? "已关闭骑马震动。" : "已恢复原版骑马震动。");
        }
        catch (Exception error)
        {
            Monitor.Log($"保存失败，继续使用原配置：{error}", LogLevel.Error);
            menu.Notify("设置保存失败，仍使用原设置。");
        }
    }

    private void Reload()
    {
        try { config = Helper.ReadConfig<ModConfig>(); }
        catch (Exception error) { Monitor.Log($"配置读取失败，继续使用原设置：{error}", LogLevel.Warn); }
    }
}
