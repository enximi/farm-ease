using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace TimeEase;

// Preview protocol: no disk writes or save calls. Authority originates only from
// the host's verified checkpoint; all participants must complete native settlement.
internal sealed class SessionNetwork
{
    private const string MessageType = "TimeEase.Checkpoint.1";
    private readonly ModEntry mod;
    private RoomConsensus? room;
    private Packet? checkpoint;
    private readonly Dictionary<long, long> readyAt = new();
    private readonly HashSet<long> departing = new();
    private long[] peers = Array.Empty<long>();
    private string? budgetDay;
    private long deadline;
    private long sendAt;
    private bool ownsPause;
    private bool clientReady;
    private bool granted;
    private bool waitingForDepartures;
    private string? lastNonce;
    private ScreenPhase? lastPhase;

    public sealed class Packet
    {
        public string Action { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Farm { get; set; } = "";
        public uint Day { get; set; }
        public bool NeedsRest { get; set; }
    }

    internal SessionNetwork(ModEntry mod)
    {
        this.mod = mod;
        mod.Helper.Events.Multiplayer.ModMessageReceived += Receive;
    }

    internal void Checkpoint(SavePoint point)
    {
        Reset("new_checkpoint");
        if (!Context.IsMainPlayer || !Context.IsMultiplayer) return;
        if (!Compatible(out var connected))
        { mod.NetworkNotice("联机保存后限制需要所有玩家使用同版且启用的 TimeEase；分屏暂只提醒。"); return; }
        peers = connected;
        deadline = Environment.TickCount64 + 120000;
        room = new(point, Members(peers), deadline);
        checkpoint = new Packet { Action = "Checkpoint", Nonce = room.Nonce, Farm = point.Farm, Day = point.Day };
        budgetDay = mod.NetworkBudgetDay;
        mod.NetworkLog("room_checkpoint", $"peers={peers.Length} game_day={point.Day}");
    }

    internal void Reset(string reason)
    {
        if (room != null && Context.IsMainPlayer && checkpoint != null)
            Send(ToPacket("Cancel"), peers);
        if (ownsPause && Context.IsWorldReady && Context.IsMainPlayer)
            Game1.netWorldState.Value.IsPaused = false;
        if (checkpoint != null) mod.NetworkLog("room_cancel", "reason=" + reason);
        room = null;
        checkpoint = null;
        ownsPause = clientReady = granted = waitingForDepartures = false;
        readyAt.Clear(); departing.Clear(); peers = Array.Empty<long>();
        budgetDay = null; lastPhase = null;
    }

    internal void Tick()
    {
        if (Context.ScreenId != 0 || checkpoint == null) return;
        if (!mod.NetworkEnabled || !Context.IsWorldReady || !Context.IsMultiplayer || Context.IsSplitScreen
            || mod.NetworkPoint != new SavePoint(checkpoint.Farm, checkpoint.Day)
            || budgetDay != mod.NetworkBudgetDay || Environment.TickCount64 > deadline)
        { Reset("context_date_or_timeout"); return; }
        if (Context.IsMainPlayer) HostTick();
        else ClientTick();
    }

    private void HostTick()
    {
        if (room == null || checkpoint == null) return;
        if (!Compatible(out var current)) { Reset("participant_unsupported"); return; }
        if (waitingForDepartures)
        {
            if (current.Except(peers).Any() || peers.Except(current).Any(id => !departing.Contains(id)))
            { Reset("unexpected_membership_change"); return; }
            if (!departing.Intersect(current).Any()) { Reset("clients_left_safely"); return; }
        }
        else if (!peers.SequenceEqual(current)) { Reset("membership_changed"); return; }
        var phase = mod.NetworkPhase;
        ReportPhase(phase);
        if (phase == ScreenPhase.Unsafe) { Reset("host_boundary_unsafe"); return; }
        // Use only validated UpdateTicking, after save callbacks and async work.
        if (!ownsPause)
        {
            if (!mod.NetworkCanPause) return;
            if (Game1.netWorldState.Value.IsPaused) { Reset("pause_already_owned"); return; }
            Game1.netWorldState.Value.IsPaused = true;
            ownsPause = true;
            mod.NetworkLog("room_pause", "owned=True");
        }
        else if (!Game1.netWorldState.Value.IsPaused) { Reset("pause_revoked"); return; }
        if (Environment.TickCount64 >= sendAt)
        {
            sendAt = Environment.TickCount64 + 250;
            if (waitingForDepartures) Send(ToPacket("Exit"), departing.ToArray());
            else Send(ToPacket("Checkpoint"), peers);
        }
        if (waitingForDepartures || phase != ScreenPhase.Ready) return;
        if (peers.Any(id => !readyAt.TryGetValue(id, out long at) || Environment.TickCount64 - at > 1000)) return;
        room.Confirm("screen:0", room.Nonce, room.Point, mod.NetworkNeedsRest);
        var result = room.Decide(Members(current), Environment.TickCount64, mod.NetworkNeedsRest);
        mod.NetworkLog("room_decision", "action=" + result.Action);
        if (result.Action == RoomAction.EndRoom)
        {
            // Same tick revalidation, while this host still owns the world pause.
            if (mod.NetworkPhase != ScreenPhase.Ready || !Game1.netWorldState.Value.IsPaused)
            { Reset("exit_boundary_changed"); return; }
            mod.NetworkLog("room_exit", "role=host reason=quota all_ready=True");
            room = null; checkpoint = null; // no cancellation or unpause before cleanup
            Game1.ExitToTitle();
        }
        else if (result.Action == RoomAction.ExitClients)
        {
            foreach (string client in result.Clients) departing.Add(long.Parse(client[5..], System.Globalization.CultureInfo.InvariantCulture));
            waitingForDepartures = true;
            deadline = Environment.TickCount64 + 15000;
            Send(ToPacket("Exit"), departing.ToArray());
        }
        else if (result.Action != RoomAction.Waiting) Reset(result.Action.ToString());
    }

    private void ClientTick()
    {
        if (checkpoint == null) return;
        if (!HostCompatible() || !Game1.HostPaused)
        {
            // A checkpoint can arrive one tick before the pause netfield.
            if (clientReady || granted) Reset("host_or_pause_changed");
            return;
        }
        var phase = mod.NetworkPhase;
        ReportPhase(phase);
        if (phase == ScreenPhase.Unsafe)
        { Send(ToPacket("NotReady"), new[] { Game1.MasterPlayer.UniqueMultiplayerID }); Reset("client_boundary_unsafe"); return; }
        if (phase != ScreenPhase.Ready) return;
        if (!clientReady) mod.NetworkLog("room_ready", "role=client native_settlement_complete=True");
        clientReady = true;
        if (granted)
        {
            if (!mod.NetworkNeedsRest) { Reset("client_quota_restored"); return; }
            mod.NetworkLog("room_exit", "role=client reason=quota host_grant=True");
            checkpoint = null;
            Game1.ExitToTitle();
            return;
        }
        if (Environment.TickCount64 >= sendAt)
        {
            sendAt = Environment.TickCount64 + 250;
            var message = ToPacket("Ready"); message.NeedsRest = mod.NetworkNeedsRest;
            Send(message, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }
    }

    private void Receive(object? sender, ModMessageReceivedEventArgs e)
    {
        if (Context.ScreenId != 0 || !mod.NetworkEnabled || !Context.IsWorldReady || !Context.IsMultiplayer
            || Context.IsSplitScreen || e.FromModID != mod.ModManifest.UniqueID || e.Type != MessageType) return;
        try
        {
            var message = e.ReadAs<Packet>();
            if (message.Nonce.Length != 32 || new SavePoint(message.Farm, message.Day) != mod.NetworkPoint) return;
            if (Context.IsMainPlayer)
            {
                if (room == null || checkpoint == null || message.Nonce != checkpoint.Nonce || !peers.Contains(e.FromPlayerID)) return;
                if (message.Action == "NotReady") { Reset("peer_not_ready"); return; }
                if (message.Action != "Ready" || waitingForDepartures || !ownsPause) return;
                if (room.Confirm("peer:" + e.FromPlayerID, message.Nonce, room.Point, message.NeedsRest))
                {
                    if (!readyAt.ContainsKey(e.FromPlayerID)) mod.NetworkLog("room_ready", $"role=peer needs_rest={message.NeedsRest} ready_peers={readyAt.Count + 1}/{peers.Length}");
                    readyAt[e.FromPlayerID] = Environment.TickCount64;
                }
            }
            else
            {
                if (e.FromPlayerID != Game1.MasterPlayer.UniqueMultiplayerID || !HostCompatible()) return;
                if (message.Action == "Checkpoint")
                {
                    if (checkpoint?.Nonce == message.Nonce) return;
                    if (lastNonce == message.Nonce) return;
                    Reset("host_checkpoint");
                    checkpoint = message; lastNonce = message.Nonce;
                    budgetDay = mod.NetworkBudgetDay;
                    deadline = Environment.TickCount64 + 120000;
                    mod.NetworkLog("room_checkpoint_received", $"game_day={message.Day}");
                }
                else if (checkpoint?.Nonce == message.Nonce)
                {
                    if (message.Action == "Cancel") Reset("host_cancelled");
                    else if (message.Action == "Exit" && clientReady) granted = true;
                }
            }
        }
        catch (Exception error)
        { mod.NetworkLog("room_message_rejected", "error=" + error.GetType().Name); Reset("invalid_message"); }
    }

    private void ReportPhase(ScreenPhase phase)
    {
        if (lastPhase == phase) return;
        lastPhase = phase;
        mod.NetworkLog("room_settlement", $"role={(Context.IsMainPlayer ? "host" : "client")} phase={phase}");
    }
    private bool HostCompatible()
    {
        var peer = mod.Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID);
        return peer?.GetMod(mod.ModManifest.UniqueID)?.Version.Equals(mod.ModManifest.Version) == true;
    }
    private bool Compatible(out long[] connected)
    {
        connected = Game1.getOnlineFarmers().Select(p => p.UniqueMultiplayerID)
            .Where(id => id != Game1.player.UniqueMultiplayerID).OrderBy(id => id).ToArray();
        if (Context.IsSplitScreen || !mod.NetworkEnabled) return false;
        return connected.All(id =>
        {
            var peer = mod.Helper.Multiplayer.GetConnectedPlayer(id);
            return peer is { IsSplitScreen: false } && peer.GetMod(mod.ModManifest.UniqueID)?.Version.Equals(mod.ModManifest.Version) == true;
        });
    }
    private static string[] Members(long[] players) => new[] { "screen:0" }.Concat(players.Select(id => "peer:" + id)).ToArray();
    private Packet ToPacket(string action) => new() { Action = action, Nonce = checkpoint!.Nonce, Farm = checkpoint.Farm, Day = checkpoint.Day };
    private void Send(Packet message, long[] recipients)
    {
        if (recipients.Length == 0) return;
        try { mod.Helper.Multiplayer.SendMessage(message, MessageType, new[] { mod.ModManifest.UniqueID }, recipients); }
        catch (Exception error)
        { mod.NetworkLog("room_send_failed", "error=" + error.GetType().Name); }
    }
}
