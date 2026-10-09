using System.Text.Json;

namespace TimeEase;

// All game installations for this OS user share this file. It is deliberately
// outside both Mods and Saves so changing farm or replacing a mod preserves it.
internal sealed class UsageStore
{
    private readonly string directory;
    private readonly string path;
    internal UsageStore(string directory) { this.directory = directory; path = Path.Combine(directory, "usage.json"); }
    internal string FilePath => path;

    private sealed class Ledger
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Dictionary<string, double>> Sessions { get; set; } = new();
        public Dictionary<string, Bonus> Bonuses { get; set; } = new();
    }

    private sealed class Bonus
    {
        public string Day { get; set; } = "";
        public int Minutes { get; set; }
    }
    internal int BonusMinutes { get; private set; }
    internal int ReadBonus(string day) { Access(null, null, day); return BonusMinutes; }
    internal int Extend(string day, int minutes, string request)
    {
        if (minutes is < 1 or > 1440 || !DateOnly.TryParseExact(day, "yyyy-MM-dd", out _) || request.Length != 32)
            throw new ArgumentException("今日加时须为 1–1440 分钟。");
        Access(null, null, day, minutes, request);
        return BonusMinutes;
    }

    internal Dictionary<string, double> Read(string session) => Access(session, null);
    internal Dictionary<string, double> Write(string session, Dictionary<string, double> seconds) => Access(session, seconds);

    private Dictionary<string, double> Access(string? session, Dictionary<string, double>? seconds, string? bonusDay = null, int minutes = 0, string? request = null)
    {
        Directory.CreateDirectory(directory);
        // No deleting the lock file: processes must always lock the same inode.
        // A competing process gets a transient IOException and retries later.
        using var lease = new FileStream(Path.Combine(directory, "usage.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        Ledger ledger;
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Version", out _)
                || !document.RootElement.TryGetProperty("Sessions", out _))
                throw new InvalidDataException("计时记录缺少必需字段；不会自动清零。");
            ledger = JsonSerializer.Deserialize<Ledger>(json)
                ?? throw new InvalidDataException("计时记录为空；不会自动清零。");
            if (ledger.Version != 1 || ledger.Sessions == null || ledger.Sessions.Any(s => s.Value == null
                || s.Value.Any(v => !DateOnly.TryParseExact(v.Key, "yyyy-MM-dd", out _) || !double.IsFinite(v.Value) || v.Value < 0)))
                throw new InvalidDataException("计时记录格式不正确；不会自动清零。");
        }
        else
        {
            if (File.Exists(path + ".bak"))
                throw new InvalidDataException("主计时记录丢失但备份仍存在；请检查文件，不自动重置额度。");
            ledger = new();
        }
        if (ledger.Bonuses == null || ledger.Bonuses.Values.Any(v => v == null
            || !DateOnly.TryParseExact(v.Day, "yyyy-MM-dd", out _) || v.Minutes is < 1 or > 1440))
            throw new InvalidDataException("今日加时记录格式错误；不会自动清零。");
        if (request != null)
        {
            if (ledger.Bonuses.TryGetValue(request, out var existing))
            {
                if (existing.Day != bonusDay || existing.Minutes != minutes) throw new InvalidDataException("加时请求内容冲突。");
            }
            else ledger.Bonuses[request] = new() { Day = bonusDay!, Minutes = minutes };
        }
        if (seconds != null || request != null)
        {
            // Absolute session totals make retries idempotent even if a previous
            // rename succeeded but the caller didn't observe its completion.
            if (seconds != null) ledger.Sessions[session!] = new(seconds);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(output, ledger);
                    output.Flush(flushToDisk: true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (bonusDay != null) BonusMinutes = checked(ledger.Bonuses.Values.Where(v => v.Day == bonusDay).Sum(v => v.Minutes));
        var totals = new Dictionary<string, double>();
        foreach (var contribution in ledger.Sessions.Where(s => s.Key != session))
            foreach (var pair in contribution.Value)
                totals[pair.Key] = totals.GetValueOrDefault(pair.Key) + pair.Value;
        return totals;
    }
}
