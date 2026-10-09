using System.Diagnostics;

namespace TimeEase;

internal sealed class SessionClock
{
    private const double ReservationSeconds = 5;
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly UsageStore store;
    private readonly DailySchedule schedule;
    private readonly Func<long> timestamp;
    private readonly Dictionary<string, double> actual = new();
    private Dictionary<string, double> others = new();
    private long previous;
    private long lastWrite;
    private bool pendingWrite;
    internal bool Running { get; private set; }

    internal SessionClock(UsageStore store, DailySchedule schedule, Func<long>? timestamp = null)
    {
        this.store = store;
        this.schedule = schedule;
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }
    internal double Used(DateTimeOffset now) => others.GetValueOrDefault(schedule.Day(now)) + actual.GetValueOrDefault(schedule.Day(now));

    internal void Start(DateTimeOffset now)
    {
        if (Running) return;
        others = store.Read(id);
        previous = timestamp();
        Running = true;
        try { Persist(now); }
        catch { Running = false; throw; }
    }

    internal void Sample(DateTimeOffset now, bool force = false)
    {
        if (!Running) return;
        long current = timestamp();
        double seconds = (current - previous) / (double)Stopwatch.Frequency;
        previous = current;
        // Stopwatch measures elapsed session time. Wall time only allocates that
        // duration to calendar days; changing the clock never subtracts usage.
        schedule.Add(actual, now, seconds);
        pendingWrite = true;
        if (force || (current - lastWrite) / (double)Stopwatch.Frequency >= ReservationSeconds / 2)
            Persist(now);
    }

    internal void Stop(DateTimeOffset now)
    {
        if (Running)
        {
            long current = timestamp();
            schedule.Add(actual, now, (current - previous) / (double)Stopwatch.Frequency);
            previous = current;
            Running = false;
        }
        pendingWrite = true;
        Persist(now); // Refund only the unconsumed reservation on a clean exit.
    }

    internal void Refresh()
    {
        if (Running) return;
        if (pendingWrite) Persist(DateTimeOffset.UtcNow);
        else others = store.Read(id);
    }

    private void Persist(DateTimeOffset now)
    {
        var reserved = new Dictionary<string, double>(actual);
        if (Running) schedule.Add(reserved, now.AddSeconds(ReservationSeconds), ReservationSeconds);
        others = store.Write(id, reserved);
        pendingWrite = false;
        lastWrite = timestamp();
    }
}
