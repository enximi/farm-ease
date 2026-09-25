using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace TravelEase;

internal sealed class SleepService
{
    private readonly ModEntry mod;
    private sealed record Request(long Player, string Home, Point Tile, string BedId, DateTime Deadline);
    private readonly PerScreen<Request?> requests = new();
    internal bool IsPending => requests.Value != null;
    internal SleepService(ModEntry mod) => this.mod = mod;
    internal void Clear() => requests.Value = null;

    internal void Start()
    {
        if (!mod.CanUse(out string reason, true)) { mod.Notify(reason); return; }
        if (Game1.newDay || Game1.player.passedOut || Game1.player.timeWentToBed.Value != 0 || Game1.player.IsSitting())
        { mod.Notify("请先结束当前休息或过夜流程，再使用一键睡觉。"); return; }
        try
        {
            var home = Utility.getHomeOfFarmer(Game1.player);
            var bed = home.GetPlayerBed();
            if (bed == null) { mod.Notify("自己的住宅里没有可用的床，请先放好床。"); return; }
            Point tile = bed.GetBedSpot();
            if (!home.isTileOnMap(tile.ToVector2())) { mod.Notify("床的位置不可用，请先调整床的位置。"); return; }
            requests.Value = new(Game1.player.UniqueMultiplayerID, home.NameOrUniqueName, tile, bed.QualifiedItemId,
                DateTime.UtcNow.AddSeconds(30));
            Game1.activeClickableMenu?.exitThisMenu(false);
            // Use the native asynchronous warp for both host and farmhands. Sleep
            // starts only after entry events and the fade have finished.
            Game1.warpFarmer(home.NameOrUniqueName, tile.X, tile.Y, 2, home.isStructure.Value);
        }
        catch (Exception error) { Fail(error); }
    }

    internal void Update()
    {
        if (requests.Value is not { } request) return;
        if (!Context.IsWorldReady || Game1.player.UniqueMultiplayerID != request.Player
            || Game1.newDay || Game1.player.passedOut || Game1.player.timeWentToBed.Value != 0)
        { Clear(); return; }
        if (DateTime.UtcNow > request.Deadline)
        { Cancel("回床等待超时，未自动睡觉；请确认当前位置后重试。"); return; }
        if (Game1.eventUp || Game1.currentMinigame != null || Game1.killScreen || Game1.player.health <= 0)
        { Cancel("当前发生了事件或状态变化，已取消自动睡觉。"); return; }
        if (Game1.locationRequest != null || Game1.fadeToBlack || Game1.fadeIn) return;
        try
        {
            if (Game1.currentLocation is not FarmHouse home || home.NameOrUniqueName != request.Home
                || Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName != request.Home
                || Game1.player.TilePoint != request.Tile)
            { Cancel("没有到达自己的床位，已取消自动睡觉。"); return; }
            var bed = home.GetPlayerBed();
            if (bed == null || bed.GetBedSpot() != request.Tile || bed.QualifiedItemId != request.BedId)
            { Cancel("床的位置或物品已改变，未自动睡觉。"); return; }

            // Entering the bed may have opened vanilla's sleep question. Only
            // dismiss that exact question; never answer another event or menu.
            if (Game1.activeClickableMenu is DialogueBox dialogue && Game1.dialogueUp
                && home.lastQuestionKey == "Sleep" && home.afterQuestion == null && Game1.afterDialogues == null)
            {
                dialogue.closeDialogue();
            }
            if (!mod.Menu.CanUse(out string reason)) { Cancel(reason + " 已取消自动睡觉。"); return; }
            Clear(); // The native ready-check owns cancellation and repeated input from here.
            if (!home.answerDialogueAction("Sleep_Yes", Array.Empty<string>()))
                mod.Notify("游戏没有接受睡觉请求，请直接使用床。");
            else mod.Monitor.Log("一键睡觉：已回到自己的床，并进入原版睡觉流程。", LogLevel.Debug);
        }
        catch (Exception error) { Fail(error); }
    }

    private void Cancel(string reason) { Clear(); mod.Notify(reason); }
    private void Fail(Exception error)
    {
        Clear();
        mod.Report(error);
        mod.Notify("一键睡觉未完成，请查看当前位置与 SMAPI 日志。");
    }
}
