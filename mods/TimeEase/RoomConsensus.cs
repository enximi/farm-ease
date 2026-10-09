namespace TimeEase;

internal enum RoomAction { Waiting, Resume, EndRoom, ExitClients, Abort }
internal sealed record RoomDecision(RoomAction Action, string[] Clients);

// No game calls: authority is granted only after the successful host checkpoint
// and an exact participant snapshot have all independently reached a safe boundary.
internal sealed class RoomConsensus
{
    internal string Nonce { get; } = Guid.NewGuid().ToString("N");
    internal SavePoint Point { get; }
    private readonly HashSet<string> participants;
    private readonly Dictionary<string, bool> ready = new();
    private readonly long deadline;
    private bool closed;
    internal RoomConsensus(SavePoint point, IEnumerable<string> participants, long deadline)
    { Point = point; this.participants = participants.ToHashSet(); this.deadline = deadline; }
    internal bool Confirm(string sender, string nonce, SavePoint point, bool needsRest)
    {
        if (closed || nonce != Nonce || point != Point || !participants.Contains(sender)) return false;
        ready[sender] = needsRest;
        return true;
    }
    internal RoomDecision Decide(IEnumerable<string> currentParticipants, long now, bool hostNeedsRest)
    {
        if (closed || now > deadline || !participants.SetEquals(currentParticipants))
        { closed = true; return new(RoomAction.Abort, Array.Empty<string>()); }
        if (participants.Count == 0 || ready.Count != participants.Count)
            return new(RoomAction.Waiting, Array.Empty<string>());
        closed = true;
        if (hostNeedsRest) return new(RoomAction.EndRoom, Array.Empty<string>());
        string[] clients = ready.Where(p => p.Key.StartsWith("peer:", StringComparison.Ordinal) && p.Value).Select(p => p.Key).ToArray();
        return new(clients.Length > 0 ? RoomAction.ExitClients : RoomAction.Resume, clients);
    }
}
