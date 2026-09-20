using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley.Menus;

namespace FishingEase;

internal static class FishingPatches
{
    private static readonly ConditionalWeakTable<BobberBar, FishingSession> Sessions = new();
    internal static Func<ModConfig> ReadConfig { get; set; } = () => new ModConfig();

    internal static FishingSession? GetSession(BobberBar menu) => Sessions.TryGetValue(menu, out var state) ? state : null;

    internal static void AfterConstruct(BobberBar __instance)
    {
        var session = new FishingSession(ReadConfig());
        Sessions.Add(__instance, session);
        session.ResizeBar(__instance);
    }

    private static void UpdateGrace(BobberBar menu, GameTime time) => GetSession(menu)?.Update(menu, time);

    private static float AdjustPenalty(float original, BobberBar menu) => original * (GetSession(menu)?.LossMultiplier ?? 1f);

    private static float AdjustBottomBounce(float original, BobberBar menu)
    {
        ModConfig? config = GetSession(menu)?.Config;
        return config?.Enabled == true ? original * config.BottomBounceMultiplier : original;
    }

    internal static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        FieldInfo button = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.buttonPressed));
        FieldInfo penalty = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.distanceFromCatchPenaltyModifier));
        FieldInfo speed = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.bobberBarSpeed));

        // Hook after the game's final in-bar calculation, before processing input.
        int buttonRead = code.FindIndex(i => i.LoadsField(button));
        int tickIndex = buttonRead - 1;
        int leadBobber = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "(O)692"));
        int bounceStore = leadBobber < 0 ? -1 : code.FindIndex(leadBobber, i => i.StoresField(speed));
        int penaltyReads = code.Count(i => i.LoadsField(penalty));
        if (tickIndex < 0 || code[tickIndex].opcode != OpCodes.Ldarg_0 || bounceStore < 0 || penaltyReads != 2)
            throw new InvalidOperationException("钓鱼更新逻辑与预期不符；停止安装补丁，避免部分生效。目标版本：Stardew Valley 1.6.15。");

        MethodInfo updateGrace = AccessTools.Method(typeof(FishingPatches), nameof(UpdateGrace));
        MethodInfo adjustPenalty = AccessTools.Method(typeof(FishingPatches), nameof(AdjustPenalty));
        MethodInfo adjustBounce = AccessTools.Method(typeof(FishingPatches), nameof(AdjustBottomBounce));
        var result = new List<CodeInstruction>();
        for (int i = 0; i < code.Count; i++)
        {
            if (i == tickIndex)
            {
                var first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(code[i].labels);
                first.blocks.AddRange(code[i].blocks);
                code[i].labels.Clear();
                code[i].blocks.Clear();
                result.Add(first);
                result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                result.Add(new CodeInstruction(OpCodes.Call, updateGrace));
            }
            if (i == bounceStore)
            {
                // Stack is [menu, original rebound speed]. Modify only the bottom collision.
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                result.Add(new CodeInstruction(OpCodes.Call, adjustBounce));
            }
            result.Add(code[i]);
            if (code[i].LoadsField(penalty))
            {
                // Preserve training rod, trap bobber and blessing calculations. No progress refund
                // after update: a refund would be too late once vanilla has already triggered escape.
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                result.Add(new CodeInstruction(OpCodes.Call, adjustPenalty));
            }
        }
        return result;
    }
}
