using System.Globalization;

namespace TimeEase;

public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public int DailyLimitMinutes { get; set; } = 60;
    public string TimeZoneId { get; set; } = "Asia/Shanghai";
    public string ResetTime { get; set; } = "00:00";
    public int WarningMinutes { get; set; } = 10;

    internal DailySchedule Validate()
    {
        if (DailyLimitMinutes is < 1 or > 1440 || WarningMinutes < 0 || WarningMinutes >= DailyLimitMinutes)
            throw new ArgumentException("每日额度须为 1–1440 分钟；提前提醒须大于等于零且小于额度。");
        if (!TimeSpan.TryParseExact(ResetTime, @"hh\:mm", CultureInfo.InvariantCulture, out var reset))
            throw new ArgumentException("重置时刻须使用 HH:mm 格式。");
        return new(TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId), reset);
    }
}

internal sealed class DailySchedule
{
    private readonly TimeZoneInfo zone;
    private readonly TimeSpan reset;
    internal DailySchedule(TimeZoneInfo zone, TimeSpan reset) { this.zone = zone; this.reset = reset; }

    internal string Day(DateTimeOffset instant)
    {
        DateTime local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        DateTime date = local.Date;
        if (instant < Boundary(date)) date = date.AddDays(-1);
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    internal DateTimeOffset NextReset(DateTimeOffset instant)
    {
        DateTime date = DateTime.ParseExact(Day(instant), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Boundary(date.AddDays(1));
    }

    private DateTimeOffset Boundary(DateTime date)
    {
        DateTime local = DateTime.SpecifyKind(date.Date + reset, DateTimeKind.Unspecified);
        // A skipped DST clock time resets at the first valid minute; a repeated
        // clock time resets once, at its first occurrence.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        TimeSpan offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    internal void Add(Dictionary<string, double> totals, DateTimeOffset end, double seconds)
    {
        DateTimeOffset cursor = end.AddSeconds(-seconds);
        while (cursor < end)
        {
            DateTimeOffset next = NextReset(cursor);
            DateTimeOffset stop = next < end ? next : end;
            string day = Day(cursor);
            totals[day] = totals.GetValueOrDefault(day) + (stop - cursor).TotalSeconds;
            cursor = stop;
        }
    }
}
