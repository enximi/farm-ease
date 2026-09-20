using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Network;

namespace StorageEase;

public sealed class StorageRequest
{
    public string Token { get; set; } = "";
    public string Action { get; set; } = "List";
    public string Box { get; set; } = "";
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
    public string[] KnownRoots { get; set; } = Array.Empty<string>();
    public string[] Boxes { get; set; } = Array.Empty<string>();
    public string Lease { get; set; } = "";
}
public sealed class StorageSnapshot
{
    public string Root { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
public sealed class StorageResponse
{
    public string Token { get; set; } = "";
    public string Message { get; set; } = "";
    public List<BoxInfo> Boxes { get; set; } = new();
    public List<StorageSnapshot> Snapshots { get; set; } = new();
    public bool Granted { get; set; }
}
public sealed class StorageDelta
{
    public string Location { get; set; } = "";
    public bool Structure { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

// Subscribe to the game's original location NetRoots. Inventory stays in the
// original chest: no item clones, temporary inventories, or changes to save layout.
internal sealed class StorageNetwork
{
    private const string RequestType = "Storage.Request.v1", ResponseType = "Storage.Response.v1";
    private const string DeltaType = "Storage.Delta.v1";
    private static readonly System.Reflection.FieldInfo MultiplayerField = AccessTools.Field(typeof(Game1), "multiplayer");
    internal static Multiplayer GameNetwork => (Multiplayer)MultiplayerField.GetValue(null)!;
    private readonly ModEntry mod;
    private bool saving;
    private sealed class State
    {
        internal List<BoxInfo> Boxes = new();
        internal readonly HashSet<string> Roots = new();
        internal string Pending = "";
        internal long SentAt, Refreshed;
        internal string Message = "";
        internal string Lease = "";
        internal Action<bool>? Acquired;
    }
    private readonly PerScreen<State> states = new(() => new());
    private readonly Dictionary<long, HashSet<string>> subscriptions = new();
    private readonly Queue<(long Player, StorageRequest Request)> incoming = new();
    private sealed record Lease(long Player, string Id, List<StardewValley.Objects.Chest> Chests)
    {
        internal long LastSeen = Environment.TickCount64;
        internal bool Closing;
    }
    private readonly Dictionary<long, Lease> leases = new();
    internal List<BoxInfo> Boxes => states.Value.Boxes;
    internal bool Loading => states.Value.Pending.Length > 0;
    internal string Message => states.Value.Message;
    internal StorageNetwork(ModEntry mod)
    {
        this.mod = mod;
        mod.Helper.Events.Multiplayer.ModMessageReceived += OnMessage;
        mod.Helper.Events.GameLoop.Saving += (_, _) => { if (Context.IsMainPlayer) saving = true; };
        mod.Helper.Events.GameLoop.Saved += (_, _) => { if (Context.IsMainPlayer) saving = false; };
        mod.Helper.Events.GameLoop.DayStarted += (_, _) => { if (Context.IsMainPlayer) saving = false; };
        mod.Helper.Events.Multiplayer.PeerDisconnected += (_, e) =>
        {
            if (!Context.IsMainPlayer) return;
            subscriptions.Remove(e.Peer.PlayerID);
            if (leases.TryGetValue(e.Peer.PlayerID, out var lease)) lease.Closing = true;
        };
        mod.Helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            if (Context.IsMainPlayer) { subscriptions.Clear(); incoming.Clear(); leases.Clear(); states.ResetAllScreens(); saving = false; }
            else states.Value = new();
        };
    }
    internal void Refresh(string box = "", string field = "", string value = "", List<BoxInfo>? leaseBoxes = null, Action<bool>? acquired = null)
    {
        if (!Context.IsWorldReady || Loading) return;
        if (!Context.IsMainPlayer)
        {
            var installed = mod.Helper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID)?.GetMod(mod.ModManifest.UniqueID);
            if (installed?.Version.Equals(mod.ModManifest.Version) != true)
            {
                states.Value.Message = "房主与使用者需要安装相同版本的随取随用。";
                states.Value.Refreshed = Environment.TickCount64;
                states.Value.Boxes.Clear(); acquired?.Invoke(false); return;
            }
        }
        var state = states.Value;
        var known = new HashSet<string>(state.Roots);
        foreach (var location in Game1.locations)
            if (StorageNetwork.GameNetwork.isAlwaysActiveLocation(location)) known.Add(location.NameOrUniqueName);
        if (Game1.currentLocation?.Root?.Value is { } current) known.Add(current.NameOrUniqueName);
        var request = new StorageRequest { Token = Guid.NewGuid().ToString("N"), Action = field.Length == 0 ? "List" : "Set",
            Box = box, Field = field, Value = value, KnownRoots = known.ToArray(), Lease = state.Lease };
        if (leaseBoxes != null)
        {
            request.Action = "Lease"; request.Boxes = leaseBoxes.Select(b => b.Id).ToArray();
            state.Lease = request.Token; state.Acquired = acquired;
        }
        state.Pending = request.Token; state.SentAt = Environment.TickCount64;
        if (Context.IsMainPlayer) incoming.Enqueue((Game1.player.UniqueMultiplayerID, request));
        else mod.Helper.Multiplayer.SendMessage(request, RequestType, new[] { mod.ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    internal void Tick()
    {
        if (!Context.IsWorldReady) return;
        if (Context.IsMainPlayer)
        {
            foreach (var lease in leases.Values.ToArray())
            {
                foreach (var chest in lease.Chests) chest.GetMutex().Update(Game1.getOnlineFarmers());
                if ((lease.Closing || Environment.TickCount64 - lease.LastSeen > 30000)
                    && lease.Chests.All(chest => !chest.GetMutex().IsLocked())) leases.Remove(lease.Player);
            }
            for (int i = 0; i < 8 && incoming.TryDequeue(out var entry); i++)
                try { Process(entry.Player, entry.Request); }
                catch (Exception error)
                {
                    mod.Report(error);
                    Reply(entry.Player, new StorageResponse { Token = entry.Request.Token, Message = "仓储同步失败，请关闭后重新打开。" });
                }
        }
        var state = states.Value;
        long now = Environment.TickCount64;
        if (Loading && now - state.SentAt > 15000)
        {
            state.Pending = ""; state.Boxes.Clear(); state.Message = "等待房主超时，请刷新仓储列表。";
            var acquired = state.Acquired; state.Acquired = null; acquired?.Invoke(false); ReleaseLease();
        }
        if (!Loading && mod.InRange && mod.HasStorageScreen && now - state.Refreshed > 5000) Refresh();
    }
    private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != mod.ModManifest.UniqueID) return;
        try
        {
            if (e.Type == RequestType && Context.IsMainPlayer && incoming.Count < 64)
                incoming.Enqueue((e.FromPlayerID, e.ReadAs<StorageRequest>()));
            else if (e.Type == ResponseType && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
                Accept(e.ReadAs<StorageResponse>());
            else if (e.Type == DeltaType && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                var delta = e.ReadAs<StorageDelta>();
                var location = Game1.getLocationFromName(delta.Location, delta.Structure);
                if (location?.Root != null && Subscribed(location))
                {
                    using var reader = new BinaryReader(new MemoryStream(delta.Data));
                    GameNetwork.readObjectDelta(reader, location.Root);
                }
            }
        }
        catch (Exception error) { mod.Report(error); }
    }
    private void Process(long player, StorageRequest request)
    {
        if (!Guid.TryParseExact(request.Token, "N", out _) || request.KnownRoots.Length > 256) return;
        var actor = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == player);
        if (actor == null) return;
        if (player != Game1.player.UniqueMultiplayerID
            && mod.Helper.Multiplayer.GetConnectedPlayer(player)?.GetMod(mod.ModManifest.UniqueID)?.Version.Equals(mod.ModManifest.Version) != true) return;
        string message = "";
        if (leases.TryGetValue(player, out var held) && held.Id == request.Lease) held.LastSeen = Environment.TickCount64;
        if (request.Action == "Release")
        {
            if (held?.Id == request.Lease) held.Closing = true;
            return;
        }
        if (saving)
        {
            Reply(player, new StorageResponse { Token = request.Token, Message = "正在保存，请稍后使用仓储。" }); return;
        }
        bool granted = false;
        if (request.Action == "Lease")
        {
            var available = StorageCatalog.List(player);
            var boxes = request.Boxes.Distinct().Select(id => available.FirstOrDefault(b => b.Id == id)).ToArray();
            if (boxes.Length == 0 || boxes.Any(b => b == null || !(request.Field == "craft" ? b.Craft : b.Remote)))
                message = "箱子权限已变化，请刷新后重试。";
            else if (held != null) message = "上一次箱子操作尚未释放，请稍后重试。";
            else
            {
                var chests = boxes.Select(b => StorageCatalog.Resolve(b!)).ToList();
                if (chests.Any(c => c == null || !StorageCatalog.Available(c) || c.GetMutex().IsLocked()))
                    message = "需要的箱子正在使用或位置已变化，请稍后重试。";
                else { leases[player] = new Lease(player, request.Token, chests.Select(c => c!).ToList()); granted = true; }
            }
        }
        if (request.Action == "Set")
        {
            var chest = StorageCatalog.All().Select(pair => pair.Chest).FirstOrDefault(chest =>
                chest.modData.TryGetValue(StorageCatalog.Prefix + "id", out string id) && id == request.Box);
            if (chest == null || StorageCatalog.Owner(chest) != player) message = "只能修改自己箱子的设置。";
            else if (chest.GetMutex().IsLocked() || chest.mutex.IsLocked()) message = "箱子正在使用，请稍后修改设置。";
            else if (request.Field is "remote" or "craft" or "shared" && request.Value is "true" or "false")
            { chest.modData[StorageCatalog.Prefix + request.Field] = request.Value; message = "箱子设置已保存。"; }
            else if (request.Field == "name" && request.Value.Length is > 0 and <= 32 && !request.Value.Any(char.IsControl))
            { chest.modData[StorageCatalog.Prefix + "name"] = request.Value; message = "箱子名称已保存。"; }
        }
        var reply = new StorageResponse { Token = request.Token, Message = message, Boxes = StorageCatalog.List(player), Granted = granted };
        if (player != Game1.player.UniqueMultiplayerID)
        {
            if (!subscriptions.TryGetValue(player, out var roots)) subscriptions[player] = roots = new();
            foreach (string rootName in reply.Boxes.Select(box => box.Root).Distinct())
            {
                var root = Game1.locations.FirstOrDefault(loc => loc.NameOrUniqueName == rootName);
                if (root == null || StorageNetwork.GameNetwork.isAlwaysActiveLocation(root)) continue;
                if (!request.KnownRoots.Contains(rootName))
                    reply.Snapshots.Add(new StorageSnapshot { Root = rootName,
                        Data = StorageNetwork.GameNetwork.writeObjectFullBytes(StorageNetwork.GameNetwork.locationRoot(root), player) });
                roots.Add(rootName);
            }
        }
        Reply(player, reply);
    }
    private void Reply(long player, StorageResponse response)
    {
        if (player == Game1.player.UniqueMultiplayerID) Accept(response);
        else mod.Helper.Multiplayer.SendMessage(response, ResponseType, new[] { mod.ModManifest.UniqueID }, new[] { player });
    }
    private void Accept(StorageResponse response)
    {
        var state = states.Value;
        // Even a late response establishes the native root subscription baseline.
        foreach (var snapshot in response.Snapshots)
        {
            using var reader = new BinaryReader(new MemoryStream(snapshot.Data));
            var root = StorageNetwork.GameNetwork.readObjectFull<GameLocation>(reader).Value;
            if (root.NameOrUniqueName != snapshot.Root) throw new InvalidDataException("仓储地图不匹配。");
            int index = Game1.locations.ToList().FindIndex(loc => loc.NameOrUniqueName == snapshot.Root);
            if (index < 0) continue;
            var previous = Game1.locations[index];
            // Don't replace an actively visited root if a warp raced the request.
            if (Game1.currentLocation?.Root?.Value.NameOrUniqueName != snapshot.Root)
            {
                Game1.locations[index] = root;
                Game1.flushLocationLookup();
                previous.OnRemoved();
            }
            state.Roots.Add(snapshot.Root);
        }
        // Roots already visited at request time need no full snapshot, but must
        // remain subscribed when the farmer later walks away from that map.
        foreach (string rootName in response.Boxes.Select(box => box.Root).Distinct())
            if (Game1.locations.Any(location => location.NameOrUniqueName == rootName && location.Root != null)) state.Roots.Add(rootName);
        if (response.Token != state.Pending) return;
        state.Pending = ""; state.Refreshed = Environment.TickCount64;
        state.Boxes = response.Boxes; state.Message = response.Message;
        var acquired = state.Acquired; state.Acquired = null;
        acquired?.Invoke(response.Granted);
    }
    internal void ReleaseLease()
    {
        var state = states.Value;
        if (state.Lease.Length == 0) return;
        var request = new StorageRequest { Token = Guid.NewGuid().ToString("N"), Action = "Release", Lease = state.Lease };
        state.Lease = "";
        if (Context.IsMainPlayer) incoming.Enqueue((Game1.player.UniqueMultiplayerID, request));
        else if (Context.IsWorldReady) mod.Helper.Multiplayer.SendMessage(request, RequestType, new[] { mod.ModManifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
    }
    internal bool IsRemoteMutex(NetMutex mutex) => leases.Values.Any(lease => lease.Chests.Any(chest => ReferenceEquals(chest.GetMutex(), mutex)));
    internal bool Subscribed(GameLocation location) => !Context.IsMainPlayer
        && states.Value.Roots.Contains(location.Root?.Value.NameOrUniqueName ?? location.NameOrUniqueName);
    internal void Forward(GameLocation loc, byte messageType, byte[] bytes)
    {
        if (!Context.IsMainPlayer || Game1.server == null || messageType != 6 || GameNetwork.isAlwaysActiveLocation(loc)) return;
        string root = loc.Root?.Value.NameOrUniqueName ?? loc.NameOrUniqueName;
        var nativeRecipients = loc.farmers.Select(f => f.UniqueMultiplayerID).ToHashSet();
        foreach (var building in loc.buildings)
            if (building.GetIndoors() is { } indoors)
                foreach (var farmer in indoors.farmers) nativeRecipients.Add(farmer.UniqueMultiplayerID);
        foreach (var pair in subscriptions)
            if (pair.Value.Contains(root) && !nativeRecipients.Contains(pair.Key) && Game1.otherFarmers.ContainsKey(pair.Key))
                // Use the same ordered SMAPI channel as the initial snapshot,
                // so a native delta cannot overtake a queued catalog callback.
                mod.Helper.Multiplayer.SendMessage(new StorageDelta { Location = loc.NameOrUniqueName,
                    Structure = loc.isStructure.Value, Data = bytes }, DeltaType,
                    new[] { mod.ModManifest.UniqueID }, new[] { pair.Key });
    }
}
