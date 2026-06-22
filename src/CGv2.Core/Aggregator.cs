namespace CGv2.Core;

public static class Aggregator
{
    public static List<DayRow> Build(
        IEnumerable<RawEvent> events,
        DateOnly today,
        DateTime now,
        int days,
        TimeOnly windowStart,
        TimeOnly windowEnd,
        DateOnly? lockDataSince)
    {
        var all = events.ToList();
        var rows = new List<DayRow>();

        for (int i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var dayEvents = all.Where(e => DateOnly.FromDateTime(e.TimestampLocal) == date).ToList();

            var boots = dayEvents.Where(e => e.Kind == EventKind.Boot)
                .Select(e => e.TimestampLocal).OrderBy(t => t).ToList();
            var shutdowns = dayEvents.Where(e => e.Kind == EventKind.Shutdown)
                .Select(e => e.TimestampLocal).OrderBy(t => t).ToList();

            TimeOnly? on = boots.Count > 0 ? TimeOnly.FromDateTime(boots[0]) : null;
            TimeOnly? off = shutdowns.Count > 0 ? TimeOnly.FromDateTime(shutdowns[^1]) : null;

            bool running = date == today && boots.Count > 0
                && (shutdowns.Count == 0 || shutdowns[^1] < boots[^1]);

            bool hasLockData = lockDataSince.HasValue && date >= lockDataSince.Value;

            var windowLocks = hasLockData
                ? ComputeWindowLocks(dayEvents, date, windowStart, windowEnd)
                : new List<LockInterval>();

            int minutes = windowLocks.Sum(l =>
                (int)Math.Round((l.End.ToTimeSpan() - l.Start.ToTimeSpan()).TotalMinutes));

            bool pcOff = dayEvents.Count == 0;

            int? workMinutes = ComputeWorkMinutes(dayEvents, date, boots, off, running, now, hasLockData);

            rows.Add(new DayRow(date, on, off, running, pcOff, hasLockData, windowLocks, minutes, workMinutes));
        }
        return rows;
    }

    private static int? ComputeWorkMinutes(
        List<RawEvent> dayEvents, DateOnly date, List<DateTime> boots,
        TimeOnly? off, bool running, DateTime now, bool hasLockData)
    {
        if (!hasLockData || boots.Count == 0) return null;
        DateTime start = boots[0];
        DateTime? end = off.HasValue ? date.ToDateTime(off.Value) : (running ? now : null);
        if (end is null || end <= start) return null;

        double uptime = (end.Value - start).TotalMinutes;
        double locked = TotalLockedMinutes(dayEvents, start, end.Value);
        int work = (int)Math.Round(uptime - locked);
        return work < 0 ? 0 : work;
    }

    private static double TotalLockedMinutes(List<RawEvent> dayEvents, DateTime lo, DateTime hi)
    {
        double total = 0;
        var ordered = dayEvents
            .Where(e => e.Kind == EventKind.Lock || e.Kind == EventKind.Unlock)
            .OrderBy(e => e.TimestampLocal).ToList();

        DateTime? lockStart = null;
        foreach (var e in ordered)
        {
            if (e.Kind == EventKind.Lock) lockStart ??= e.TimestampLocal;
            else if (lockStart.HasValue)
            {
                total += ClipMinutes(lockStart.Value, e.TimestampLocal, lo, hi);
                lockStart = null;
            }
        }
        if (lockStart.HasValue) total += ClipMinutes(lockStart.Value, hi, lo, hi);
        return total;
    }

    private static double ClipMinutes(DateTime start, DateTime end, DateTime lo, DateTime hi)
    {
        var s = start < lo ? lo : start;
        var e = end > hi ? hi : end;
        return e <= s ? 0 : (e - s).TotalMinutes;
    }

    private static List<LockInterval> ComputeWindowLocks(
        List<RawEvent> dayEvents, DateOnly date, TimeOnly windowStart, TimeOnly windowEnd)
    {
        var result = new List<LockInterval>();
        var ordered = dayEvents
            .Where(e => e.Kind == EventKind.Lock || e.Kind == EventKind.Unlock)
            .OrderBy(e => e.TimestampLocal)
            .ToList();

        DateTime winStart = date.ToDateTime(windowStart);
        DateTime winEnd = date.ToDateTime(windowEnd);

        DateTime? lockStart = null;
        foreach (var e in ordered)
        {
            if (e.Kind == EventKind.Lock)
                lockStart ??= e.TimestampLocal;
            else if (lockStart.HasValue)
            {
                AddClipped(result, lockStart.Value, e.TimestampLocal, winStart, winEnd, isOpen: false);
                lockStart = null;
            }
        }
        if (lockStart.HasValue)
            AddClipped(result, lockStart.Value, winEnd, winStart, winEnd, isOpen: true);

        return result;
    }

    private static void AddClipped(List<LockInterval> result, DateTime start, DateTime end,
        DateTime winStart, DateTime winEnd, bool isOpen)
    {
        var s = start < winStart ? winStart : start;
        var ee = end > winEnd ? winEnd : end;
        if (ee <= s) return;
        result.Add(new LockInterval(TimeOnly.FromDateTime(s), TimeOnly.FromDateTime(ee), isOpen));
    }
}
