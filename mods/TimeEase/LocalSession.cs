using StardewModdingAPI;
using StardewValley;

namespace TimeEase;

// Only a verified host checkpoint can start this local-only coordinator.
// Each screen reports in its own SMAPI update context; never switch Game1 instances.
internal sealed class LocalSession
{
    private readonly ModEntry mod;
    private SavePoint? checkpoint;
    private string? budgetDay;
    private int[] members = Array.Empty<int>();
    private readonly Dictionary<int, (SavePoint Point, ScreenPhase Phase, long At)> reports = new();
    private long deadline;
    private bool ownsPause;
    private string? lastReport;

    internal LocalSession(ModEntry mod) => this.mod = mod;

    internal void Checkpoint(SavePoint point)
    {
        Reset("new_checkpoint");
        if (!Context.IsMainPlayer || !Context.IsSplitScreen || Context.HasRemotePlayers || !mod.NetworkNeedsRest) return;
        checkpoint = point;
        budgetDay = mod.NetworkBudgetDay;
        members = ActiveScreens();
        deadline = Environment.TickCount64 + 120000;
        mod.NetworkLog("local_checkpoint", $"screens={members.Length} game_day={point.Day}");
    }

    internal void Reset(string reason)
    {
        if (ownsPause && Context.IsWorldReady && Context.IsMainPlayer)
            Game1.netWorldState.Value.IsPaused = false;
        if (checkpoint.HasValue) mod.NetworkLog("local_cancel", "reason=" + reason);
        checkpoint = null; budgetDay = null; members = Array.Empty<int>();
        reports.Clear(); ownsPause = false; lastReport = null;
    }

    internal void Tick()
    {
        if (!checkpoint.HasValue) return;
        if (!Context.IsWorldReady)
        { if (Context.ScreenId == 0) Reset("host_world_unavailable"); return; }
        if (Context.ScreenId != Game1.game1.instanceId)
        { if (Context.ScreenId == 0) Reset("screen_identity_unsupported"); return; }
        var phase = mod.NetworkPhase;
        if (Context.ScreenId != 0 && phase == ScreenPhase.Ready && !Game1.HostPaused)
            phase = ScreenPhase.NativeTransition;
        reports[Context.ScreenId] = (mod.NetworkPoint, phase, Environment.TickCount64);
        if (Context.ScreenId != 0) return;
        if (!Context.IsMainPlayer || !Context.IsSplitScreen || Context.HasRemotePlayers
            || !mod.NetworkEnabled || !mod.NetworkNeedsRest || budgetDay != mod.NetworkBudgetDay
            || !members.SequenceEqual(ActiveScreens()) || Environment.TickCount64 > deadline)
        { Reset("mode_members_quota_date_or_timeout"); return; }
        if (mod.NetworkPoint != checkpoint.Value || phase == ScreenPhase.Unsafe)
        { Reset("host_boundary_changed"); return; }
        if (!ownsPause)
        {
            if (!mod.NetworkCanPause) return;
            if (Game1.netWorldState.Value.IsPaused) { Reset("pause_already_owned"); return; }
            Game1.netWorldState.Value.IsPaused = true;
            ownsPause = true;
            mod.NetworkLog("local_pause", "owned=True");
        }
        else if (!Game1.netWorldState.Value.IsPaused) { Reset("pause_revoked"); return; }
        foreach (int id in members)
        {
            if (!reports.TryGetValue(id, out var report) || Environment.TickCount64 - report.At > 250) return;
            if (report.Point != checkpoint.Value || report.Phase == ScreenPhase.Unsafe)
            { Reset("screen_boundary_changed"); return; }
        }
        int ready = members.Count(id => reports[id].Phase == ScreenPhase.Ready);
        string progress = $"ready={ready}/{members.Length}";
        if (lastReport != progress) { lastReport = progress; mod.NetworkLog("local_ready", progress); }
        if (ready != members.Length) return;
        // Host alone invokes ExitToTitle; its native cleanup removes local screens.
        // Keep our pause through cleanup so nobody can play in the saved new day.
        if (mod.NetworkPhase != ScreenPhase.Ready || !Game1.netWorldState.Value.IsPaused) return;
        mod.NetworkLog("local_exit", $"role=host all_ready=True screens={members.Length}");
        checkpoint = null;
        Game1.ExitToTitle();
    }

    private static int[] ActiveScreens() => GameRunner.instance.gameInstances.Select(game => game.instanceId).OrderBy(id => id).ToArray();
}
