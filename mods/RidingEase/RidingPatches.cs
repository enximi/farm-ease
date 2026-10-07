using System.Reflection.Emit;
using HarmonyLib;
using StardewValley;

namespace RidingEase;

internal static class RidingPatches
{
    internal static Func<bool> ShouldSuppress { get; set; } = () => true;

    internal static IEnumerable<CodeInstruction> FootstepTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var rumble = AccessTools.DeclaredMethod(typeof(Rumble), nameof(Rumble.rumble), new[] { typeof(float), typeof(float) })
            ?? throw new MissingMethodException("未找到原版震动方法。");
        var replacement = AccessTools.DeclaredMethod(typeof(RidingPatches), nameof(HorseRumble));
        // 仅替换原版马蹄方法内木地、石地及其他地面的三次调用，保留音效与本屏骑手判断。
        // 调用签名相同，原指令的标签和异常块继续保留；版本不匹配时整项补丁停止加载。
        if (code.Count(instruction => instruction.Calls(rumble)) != 3)
            throw new InvalidOperationException("马蹄震动逻辑与预期不符；停止安装补丁。目标版本：Stardew Valley 1.6.15。");
        foreach (var instruction in code)
        {
            if (!instruction.Calls(rumble)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
        }
        return code;
    }

    private static void HorseRumble(float power, float milliseconds)
    {
        // 不关闭全局震动，也不每帧清空马达，避免打断其他来源的反馈。
        if (!ShouldSuppress()) Rumble.rumble(power, milliseconds);
    }
}
