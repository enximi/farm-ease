namespace TimeEase;

internal enum PlayMode { SinglePlayer, LocalHost, RemoteHost, RemoteClient }
internal enum ScreenPhase { NativeTransition, Ready, Unsafe }
internal enum BoundaryDecision { None, Waiting, Continue, Rest, Unsupported }

// Shared coordination is independent of SMAPI and never writes game state.
// Remote and split-screen runtime coordination uses separate consensus owners.
internal sealed class RestBoundary
{
    private SaveEvidence? attempt;
    private SavePoint? pending;
    private string? budgetDay;
    private readonly Dictionary<int, (SavePoint Point, ScreenPhase Phase)> screens = new();
    internal bool HasPending => pending.HasValue;

    internal SaveEvidence BeginSave(SavePoint point)
    {
        Clear();
        return attempt = new(point);
    }
    internal bool SaveObserved => attempt?.Observed == true;
    internal bool Saved(SavePoint point, string day)
    {
        if (attempt?.Point != point || !attempt.Succeeded) { Clear(); return false; }
        SetBoundary(point, day);
        attempt = null;
        return true;
    }
    internal void Loaded(SavePoint point, string day, bool readFromDisk)
    {
        if (readFromDisk) { Clear(); SetBoundary(point, day); }
        // New-character creation is not proof of a disk checkpoint. It can only
        // retain evidence from its successfully observed initial save.
    }
    private void SetBoundary(SavePoint point, string day)
    {
        pending = point;
        budgetDay = day;
        screens.Clear();
    }
    internal void Report(int screen, SavePoint point, ScreenPhase phase) => screens[screen] = (point, phase);
    internal BoundaryDecision Evaluate(string day, PlayMode mode, bool enableLocalHost,
        IReadOnlyCollection<int> activeScreens, bool shouldRest)
    {
        if (!pending.HasValue) return BoundaryDecision.None;
        if (day != budgetDay) return Finish(BoundaryDecision.Continue);
        if (mode is PlayMode.RemoteHost or PlayMode.RemoteClient || (mode == PlayMode.LocalHost && !enableLocalHost))
            return Finish(BoundaryDecision.Unsupported);
        if (activeScreens.Count == 0) return BoundaryDecision.Waiting;
        foreach (int id in activeScreens)
        {
            if (!screens.TryGetValue(id, out var screen)) return BoundaryDecision.Waiting;
            if (screen.Point != pending.Value || screen.Phase == ScreenPhase.Unsafe)
                return Finish(BoundaryDecision.Unsupported);
            if (screen.Phase != ScreenPhase.Ready) return BoundaryDecision.Waiting;
        }
        return Finish(shouldRest ? BoundaryDecision.Rest : BoundaryDecision.Continue);
    }
    private BoundaryDecision Finish(BoundaryDecision result) { Clear(); return result; }
    internal void Clear()
    {
        attempt?.Fail();
        attempt = null;
        pending = null;
        budgetDay = null;
        screens.Clear();
    }
}
