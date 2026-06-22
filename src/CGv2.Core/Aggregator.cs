namespace CGv2.Core;

public static class Aggregator
{
    public static List<DayRow> Build(
        IEnumerable<RawEvent> events,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        DateTime now,
        DateOnly? lockDataSince)
    {
        var all = events.ToList();
        var rows = new List<DayRow>();

        for (var date = to; date >= from; date = date.AddDays(-1))
        {
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
            bool pcOff = dayEvents.Count == 0;

            // Effective uptime span: first boot -> (last shutdown, or now if still running).
            DateTime? start = boots.Count > 0 ? boots[0] : null;
            DateTime? end = running ? now : (shutdowns.Count > 0 ? shutdowns[^1] : null);
            bool hasSpan = start.HasValue && end.HasValue && end.Value > start.Value;

            // Pause = lock intervals cut out of the uptime span.
            var dayLocks = hasSpan ? ClipLocks(dayEvents, start!.Value, end!.Value) : new List<LockInterval>();
            int pauseMinutes = dayLocks.Sum(l =>
                (int)Math.Round((l.End.ToTimeSpan() - l.Start.ToTimeSpan()).TotalMinutes));

            // Work = uptime - pause. Shown whenever we know the uptime span, even without lock data
            // (then pause is 0 -> gross uptime).
            int? workMinutes = hasSpan
                ? Math.Max(0, (int)Math.Round((end!.Value - start!.Value).TotalMinutes) - pauseMinutes)
                : null;

            TimeOnly? barEnd = hasSpan ? TimeOnly.FromDateTime(end!.Value) : null;

            rows.Add(new DayRow(date, on, off, running, pcOff, hasLockData, dayLocks, pauseMinutes, workMinutes, barEnd));
        }
        return rows;
    }

    // Lock->Unlock intervals clipped to [lo, hi]; an open lock (no unlock) is clamped to hi.
    private static List<LockInterval> ClipLocks(List<RawEvent> dayEvents, DateTime lo, DateTime hi)
    {
        var result = new List<LockInterval>();
        var ordered = dayEvents
            .Where(e => e.Kind == EventKind.Lock || e.Kind == EventKind.Unlock)
            .OrderBy(e => e.TimestampLocal)
            .ToList();

        DateTime? lockStart = null;
        foreach (var e in ordered)
        {
            if (e.Kind == EventKind.Lock)
                lockStart ??= e.TimestampLocal;
            else if (lockStart.HasValue)
            {
                AddClipped(result, lockStart.Value, e.TimestampLocal, lo, hi, isOpen: false);
                lockStart = null;
            }
        }
        if (lockStart.HasValue)
            AddClipped(result, lockStart.Value, hi, lo, hi, isOpen: true);

        return result;
    }

    private static void AddClipped(List<LockInterval> result, DateTime start, DateTime end,
        DateTime lo, DateTime hi, bool isOpen)
    {
        var s = start < lo ? lo : start;
        var e = end > hi ? hi : end;
        if (e <= s) return;
        result.Add(new LockInterval(TimeOnly.FromDateTime(s), TimeOnly.FromDateTime(e), isOpen));
    }
}
