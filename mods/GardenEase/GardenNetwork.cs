using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace GardenEase;

internal sealed class GardenRequest
{
    public int Protocol { get; set; } = 1;
    public string Session { get; set; } = "";
    public int Sequence { get; set; }
    public string Action { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Expected { get; set; } = "";
}
internal sealed class GardenReply
{
    public string Session { get; set; } = "";
    public int Sequence { get; set; }
    public string Action { get; set; } = "";
    public bool Open { get; set; }
    public bool Selected { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int UndoCount { get; set; }
    public string Message { get; set; } = "";
    public bool Restored { get; set; }
}

// Only the host mutates farm state, processing each command to completion.
// Editors have independent sessions and reserve only their selected source tile.
internal sealed class GardenNetwork
{
    private const string RequestType = "Garden.Request.v1", ReplyType = "Garden.Reply.v1";
    private readonly ModEntry mod;
    private sealed class Client
    {
        internal ArrangeMenu? Menu;
        internal string Session = "";
        internal int Sequence;
        internal GardenRequest? Pending;
        internal long SentAt, StartedAt, Heartbeat;
    }
    private sealed class HostSession
    {
        internal readonly long Player;
        internal readonly string Id;
        internal readonly MoveSession Moves;
        internal readonly Dictionary<(bool Objects, Vector2 Tile), object> Known = new();
        internal long LastSeen = Environment.TickCount64;
        internal int Sequence;
        internal GardenReply? LastReply;
        internal HostSession(long player, string id, Farm farm, Farmer actor)
        {
            Player = player; Id = id; Moves = new(farm, actor);
            foreach (var pair in farm.objects.Pairs) Known[(true, pair.Key)] = pair.Value;
            foreach (var pair in farm.terrainFeatures.Pairs) Known[(false, pair.Key)] = pair.Value;
        }
        internal bool Unchanged(Vector2 tile, bool objects)
        {
            object? now = Read(tile, objects);
            Known.TryGetValue((objects, tile), out var known);
            return ReferenceEquals(now, known);
        }
        private object? Read(Vector2 tile, bool objects) => objects
            ? Moves.Farm.objects.TryGetValue(tile, out var obj) ? obj : null
            : Moves.Farm.terrainFeatures.TryGetValue(tile, out var terrain) ? terrain : null;
        internal void Observe(Vector2 tile)
        {
            foreach (bool objects in new[] { false, true })
                if (Read(tile, objects) is { } value) Known[(objects, tile)] = value;
                else Known.Remove((objects, tile));
        }
    }
    private readonly PerScreen<Client> clients = new(() => new());
    private readonly Queue<(long Player, GardenRequest Request)> incoming = new();
    private readonly Dictionary<long, HostSession> sessions = new();
    private bool saving;
    internal GardenNetwork(ModEntry mod)
    {
        this.mod = mod;
        var events = mod.Helper.Events;
        events.Multiplayer.ModMessageReceived += OnMessage;
        events.Multiplayer.PeerDisconnected += (_, e) => { if (Context.IsMainPlayer) sessions.Remove(e.Peer.PlayerID); };
        events.GameLoop.UpdateTicked += (_, _) => Tick();
        events.GameLoop.Saving += (_, _) => { if (Context.IsMainPlayer) { saving = true; sessions.Clear(); incoming.Clear(); } };
        events.GameLoop.DayStarted += (_, _) => { if (Context.IsMainPlayer) { saving = false; sessions.Clear(); incoming.Clear(); } };
        events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            if (Context.IsMainPlayer) { sessions.Clear(); incoming.Clear(); clients.ResetAllScreens(); saving = false; }
            else clients.Value = new();
        };
    }

    internal string? UnavailableReason()
    {
        if (!Context.IsMultiplayer || Context.IsMainPlayer) return null;
        var peer = mod.Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID);
        var installed = peer?.GetMod(mod.ModManifest.UniqueID);
        if (installed == null) return "多人整理需要房主也安装田园巧整。";
        if (!installed.Version.Equals(mod.ModManifest.Version)) return "请将房主和参与整理的玩家更新到相同版本的田园巧整。";
        return null;
    }
    internal void Open(ArrangeMenu menu)
    {
        var state = clients.Value;
        state.Menu = menu; state.Session = Guid.NewGuid().ToString("N"); state.Sequence = 0;
        state.Pending = null; state.Heartbeat = Environment.TickCount64;
        Send("Open", Point.Zero, "");
    }
    internal bool Send(string action, Point tile, string expected)
    {
        var state = clients.Value;
        if (state.Menu == null || state.Pending != null) return false;
        var request = new GardenRequest { Session = state.Session, Sequence = ++state.Sequence,
            Action = action, X = tile.X, Y = tile.Y, Expected = expected };
        state.Pending = request; state.StartedAt = state.SentAt = Environment.TickCount64;
        state.Menu.WaitingForHost = true;
        Dispatch(request);
        return true;
    }
    internal void Close(ArrangeMenu menu)
    {
        var state = clients.Value;
        if (!ReferenceEquals(state.Menu, menu)) return;
        Dispatch(new GardenRequest { Session = state.Session, Action = "Close" });
        state.Menu = null; state.Pending = null;
    }
    private void Dispatch(GardenRequest request)
    {
        if (Context.IsMainPlayer) incoming.Enqueue((Game1.player.UniqueMultiplayerID, request));
        else mod.Helper.Multiplayer.SendMessage(request, RequestType, new[] { mod.ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != mod.ModManifest.UniqueID) return;
        try
        {
            if (e.Type == RequestType && Context.IsMainPlayer)
            {
                // Bound queued requests; malformed or excessive peer traffic can't grow memory indefinitely.
                if (incoming.Count < 128) incoming.Enqueue((e.FromPlayerID, e.ReadAs<GardenRequest>()));
            }
            else if (e.Type == ReplyType && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
                Accept(e.ReadAs<GardenReply>());
        }
        catch (Exception error) { mod.Report(error); }
    }
    private void Accept(GardenReply reply)
    {
        var state = clients.Value;
        if (state.Menu == null || reply.Session != state.Session) return;
        if (reply.Action == "Ping")
        {
            // Idle status refreshes undo counts after another editor's changes.
            // Never let an older heartbeat overwrite an in-flight command reply.
            if (reply.Sequence == state.Sequence && state.Pending == null) state.Menu.Receive(reply);
            return;
        }
        if (reply.Sequence != state.Pending?.Sequence) return;
        state.Pending = null;
        state.Menu.WaitingForHost = false;
        state.Menu.Receive(reply);
    }
    private void Reply(long player, GardenReply reply)
    {
        if (player == Game1.player.UniqueMultiplayerID) Accept(reply);
        else mod.Helper.Multiplayer.SendMessage(reply, ReplyType, new[] { mod.ModManifest.UniqueID }, new[] { player });
    }
    private void Tick()
    {
        if (!Context.IsWorldReady) return;
        long now = Environment.TickCount64;
        if (Context.IsMainPlayer)
        {
            foreach (var session in sessions.Values.ToArray())
                if (now - session.LastSeen > 30000 || !CanEdit(session.Player, out var editor)
                    || !ReferenceEquals(editor?.currentLocation, session.Moves.Farm)) sessions.Remove(session.Player);
            for (int i = 0; i < 16 && incoming.TryDequeue(out var entry); i++)
            {
                try { Process(entry.Player, entry.Request); }
                catch (Exception error)
                {
                    mod.Report(error); sessions.Remove(entry.Player);
                    Reply(entry.Player, new GardenReply { Session = entry.Request.Session, Sequence = entry.Request.Sequence,
                        Action = entry.Request.Action, Message = "整理操作中断，请检查现场后重新打开。详情见 SMAPI 日志。" });
                }
            }
        }
        var state = clients.Value;
        if (state.Menu == null) return;
        if (!ReferenceEquals(Game1.activeClickableMenu, state.Menu)) { Close(state.Menu); return; }
        if (state.Pending is { } pending)
        {
            if (now - state.StartedAt > 15000)
            {
                var menu = state.Menu;
                Close(menu);
                menu.Receive(new GardenReply { Message = "未收到房主确认；请检查现场状态后重新打开整理。" });
            }
            else if (now - state.SentAt > 2500) { state.SentAt = now; Dispatch(pending); }
        }
        else if (now - state.Heartbeat > 5000)
        {
            state.Heartbeat = now;
            Dispatch(new GardenRequest { Session = state.Session, Sequence = state.Sequence, Action = "Ping" });
        }
    }
    private bool CanEdit(long player, out Farmer? actor)
    {
        actor = Game1.getOnlineFarmers().FirstOrDefault(farmer => farmer.UniqueMultiplayerID == player);
        return !saving && actor?.currentLocation is Farm && actor.health > 0 && !actor.UsingTool
            && !actor.isRidingHorse() && !actor.swimming.Value && !Game1.isFestival() && !Game1.eventUp;
    }
    private void Process(long player, GardenRequest request)
    {
        if (request.Protocol != 1 || !Guid.TryParseExact(request.Session, "N", out _)
            || string.IsNullOrEmpty(request.Action) || request.Action.Length > 16) return;
        var reply = new GardenReply { Session = request.Session, Sequence = request.Sequence, Action = request.Action };
        if (request.Action == "Close")
        {
            if (sessions.TryGetValue(player, out var closing) && closing.Id == request.Session) sessions.Remove(player);
            return;
        }
        if (!CanEdit(player, out Farmer? actor))
        {
            reply.Message = "请先回到农场室外，结束工具、活动或过夜操作。";
            Reply(player, reply); return;
        }
        if (request.Action == "Open")
        {
            if (player != Game1.player.UniqueMultiplayerID
                && mod.Helper.Multiplayer.GetConnectedPlayer(player)?.GetMod(mod.ModManifest.UniqueID)?.Version.Equals(mod.ModManifest.Version) != true)
            {
                reply.Message = "房主与参与整理的玩家需要安装相同版本的田园巧整。";
                Reply(player, reply); return;
            }
            if (sessions.TryGetValue(player, out var existing) && existing.Id != request.Session)
            {
                reply.Message = "你上一次的整理会话尚未结束，请稍后重新打开。";
                Reply(player, reply); return;
            }
            if (request.Sequence != 1) { reply.Message = "整理请求顺序异常，请重新打开。"; Reply(player, reply); return; }
            if (existing == null) sessions.Add(player, new HostSession(player, request.Session, (Farm)actor!.currentLocation, actor));
        }
        if (!sessions.TryGetValue(player, out var session) || session.Id != request.Session)
        {
            reply.Message = "整理会话已结束，请重新打开。";
            Reply(player, reply); return;
        }
        if (!ReferenceEquals(actor!.currentLocation, session.Moves.Farm))
        {
            sessions.Remove(player); reply.Message = "所在地图已改变，请重新打开整理。"; Reply(player, reply); return;
        }
        session.LastSeen = Environment.TickCount64;
        if (request.Action == "Ping")
        {
            reply.Open = true; PopulateReply(session, reply); Reply(player, reply); return;
        }
        if (request.Sequence == session.Sequence && session.LastReply != null) { Reply(player, session.LastReply); return; }
        if (request.Sequence != session.Sequence + 1) { reply.Message = "整理请求顺序异常，请重新打开。"; Reply(player, reply); return; }
        reply.Open = true;
        Vector2 tile = new(request.X, request.Y);
        var moves = session.Moves;
        switch (request.Action)
        {
            case "Open": reply.Message = "已连接房主，可以与其他玩家同时整理。时间继续流逝。"; break;
            case "Cancel": moves.Cancel(); reply.Message = "已取消选中，对象仍在原位置。"; break;
            case "Select":
                if (!moves.Farm.isTileOnMap(tile)) { reply.Message = "超出农场边界。"; break; }
                if (ReservedByOther(session, new[] { tile }) is string selectConflict) { reply.Message = selectConflict; break; }
                if (!session.Unchanged(tile, true) || !session.Unchanged(tile, false)
                    || request.Expected != Describe(moves.Farm, tile))
                {
                    session.Observe(tile); moves.Cancel();
                    reply.Message = "这一格已发生变化，请等同步后重新选择。"; break;
                }
                moves.Cancel(); reply.Message = moves.Select(tile); break;
            case "Place":
                if (!moves.Farm.isTileOnMap(tile) || moves.Selected == null || moves.Source is not Vector2 source)
                { reply.Message = "请重新选择需要搬移的对象。"; break; }
                if (ReservedByOther(session, new[] { source, tile }) is string placeConflict) { reply.Message = placeConflict; break; }
                if (!session.Unchanged(tile, true) || !session.Unchanged(tile, false)
                    || request.Expected != Describe(moves.Farm, tile, moves.Selected.ObjectLayer))
                {
                    session.Observe(tile); reply.Message = "目标格已发生变化，请等同步后再次确认。"; break;
                }
                int previousUndoCount = moves.UndoCount;
                reply.Message = moves.Place(tile);
                if (moves.UndoCount > previousUndoCount) LayoutChanged(session);
                session.Observe(source); session.Observe(tile); break;
            case "Undo":
                if (ReservedByOther(session, moves.UndoTiles) is string undoConflict) { reply.Message = undoConflict; break; }
                reply.Message = moves.Undo(out var restored);
                if (restored is Vector2 position)
                {
                    reply.Restored = true; reply.X = (int)position.X; reply.Y = (int)position.Y;
                    LayoutChanged(session);
                    // Our own undo changes the known layout; retain stale references elsewhere.
                    foreach (Vector2 changed in moves.LastChanged) session.Observe(changed);
                }
                break;
            default: reply.Message = "未知整理操作。"; break;
        }
        PopulateReply(session, reply);
        session.Sequence = request.Sequence; session.LastReply = reply;
        Reply(player, reply);
    }
    private string? ReservedByOther(HostSession session, IEnumerable<Vector2> tiles)
    {
        var affected = tiles.ToHashSet();
        foreach (var other in sessions.Values)
            if (other.Player != session.Player && ReferenceEquals(other.Moves.Farm, session.Moves.Farm)
                && other.Moves.Source is Vector2 source && affected.Contains(source)
                && other.Moves.Selected?.IsAt(other.Moves.Farm, source) == true)
                return "这格已被其他玩家选中，请选择别处，或等对方放下 / 取消。";
        return null;
    }
    private void LayoutChanged(HostSession session)
    {
        var changed = session.Moves.LastChanged.ToHashSet();
        foreach (var other in sessions.Values)
            if (other.Player != session.Player && ReferenceEquals(other.Moves.Farm, session.Moves.Farm))
                other.Moves.InvalidateUndo(changed);
        // Keep other editors' Known snapshots unchanged: their next command at
        // an affected tile must notice the change and require a fresh confirmation.
    }
    private static void PopulateReply(HostSession session, GardenReply reply)
    {
        reply.Selected = session.Moves.Selected != null;
        if (session.Moves.Source is Vector2 selected) { reply.X = (int)selected.X; reply.Y = (int)selected.Y; }
        reply.UndoCount = session.Moves.UndoCount;
    }
    internal static string Describe(Farm farm, Vector2 tile, bool? objectLayer = null)
    {
        var item = ArrangeItem.Read(farm, tile, objectLayer);
        if (item?.IsTree == true)
        {
            string species = item.Value is StardewValley.TerrainFeatures.Tree tree ? tree.treeType.Value
                : ((StardewValley.TerrainFeatures.FruitTree)item.Value).treeId.Value;
            return item.Value.GetType().FullName + ":" + species + ":" + (item.Tapper?.QualifiedItemId ?? "");
        }
        if (objectLayer != false && farm.objects.TryGetValue(tile, out var obj)) return obj.GetType().FullName + ":" + obj.QualifiedItemId;
        if (objectLayer == true) return "";
        if (!farm.terrainFeatures.TryGetValue(tile, out var terrain)) return "";
        return terrain.GetType().FullName + ":" + (terrain is StardewValley.TerrainFeatures.Flooring floor ? floor.whichFloor.Value
            : terrain is StardewValley.TerrainFeatures.HoeDirt dirt ? dirt.crop?.indexOfHarvest.Value ?? "" : "");
    }
}
