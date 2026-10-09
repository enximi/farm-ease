namespace TimeEase;

internal readonly record struct SavePoint(string Farm, uint Day);

// A token belongs to one save attempt and one exact native state machine.
// Only the inspected inner MoveNext can record its successful terminal marker.
internal sealed class SaveEvidence
{
    private int state; // 0 created, 1 observing, 2 committed, 3 failed
    private object? source;
    private int sourceConflict;
    internal SavePoint Point { get; }
    internal SaveEvidence(SavePoint point) => Point = point;
    internal bool Succeeded => Volatile.Read(ref state) == 2;
    internal bool Observed => Volatile.Read(ref source) != null;
    internal bool SourceConflict => Volatile.Read(ref sourceConflict) != 0;
    internal bool Observe(object iterator)
    {
        object? previous = Interlocked.CompareExchange(ref source, iterator, null);
        if (previous != null && !ReferenceEquals(previous, iterator))
        {
            Interlocked.Exchange(ref sourceConflict, 1);
            Fail();
            return false;
        }
        Interlocked.CompareExchange(ref state, 1, 0);
        return Volatile.Read(ref state) is 1 or 2;
    }
    internal void Complete() => Interlocked.CompareExchange(ref state, 2, 1);
    internal void Fail() => Interlocked.Exchange(ref state, 3);
}
