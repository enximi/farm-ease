using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;

namespace TravelEase;

internal sealed class MountedTravel
{
    private sealed record WarpScope(Farmer Rider, Horse Horse, GameLocation Source, GameLocation Target);
    private static readonly PerScreen<WarpScope?> scopes = new();
    internal bool IsReady { get; }

    internal MountedTravel(ModEntry mod)
    {
        var harmony = new Harmony(mod.ModManifest.UniqueID + ".MountedTravel");
        try
        {
            var method = AccessTools.DeclaredMethod(typeof(Game1), nameof(Game1.ShouldDismountOnWarp),
                new[] { typeof(Horse), typeof(GameLocation), typeof(GameLocation) })
                ?? throw new MissingMethodException("未找到原版传送下马判断。");
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(MountedTravel), nameof(KeepMounted)));
            IsReady = true;
        }
        catch (Exception error)
        {
            harmony.UnpatchAll(harmony.Id);
            mod.Monitor.Log($"骑马传送未启用，步行传送仍可使用：{error}", LogLevel.Error);
        }
    }

    private static void KeepMounted(Horse __0, GameLocation __1, GameLocation __2, ref bool __result)
    {
        // 原版在 warpFarmer 中同步判断是否下马；只豁免本 Mod 正在提交的这次传送。
        // 保留原玩家 netMount 与马的实例，原生网络负责携带坐骑，不另造或搬运地图 NPC。
        if (scopes.Value is { } scope && ReferenceEquals(Game1.player, scope.Rider)
            && ReferenceEquals(__0, scope.Horse) && ReferenceEquals(scope.Rider.mount, scope.Horse)
            && ReferenceEquals(__1, scope.Source) && ReferenceEquals(__2, scope.Target)) __result = false;
    }

    internal void Warp(GameLocation target, Vector2 landing)
    {
        var previous = scopes.Value;
        try
        {
            scopes.Value = Game1.player.mount is { } horse
                ? new(Game1.player, horse, Game1.currentLocation, target) : null;
            Game1.warpFarmer(target.NameOrUniqueName, (int)landing.X, (int)landing.Y, 2, target.isStructure.Value);
        }
        finally { scopes.Value = previous; }
    }

    internal static Rectangle LandingBounds(Vector2 tile)
    {
        if (Game1.player.mount is not { } horse)
            return new Rectangle((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
        // 使用原版传送的玩家像素落点及马的自然碰撞框，不临时改写活马位置。
        // 不沿用过窄门时缩小的碰撞框，落地后恢复正常宽度也必须放得下。
        int y = (int)tile.Y * 64 - (Game1.player.Sprite.getHeight() - 32) + 16;
        return new Rectangle((int)tile.X * 64 + 8, y + 16, horse.GetSpriteWidthForPositioning() * 3, 32);
    }
}
