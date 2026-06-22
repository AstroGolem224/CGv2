using CGv2.Core;
using Xunit;

public class AggregatorTests
{
    static readonly DateOnly Day = new(2026, 6, 22);
    static readonly DateTime Now = Day.ToDateTime(new TimeOnly(23, 59));

    static RawEvent Ev(EventKind k, int h, int m) => new(k, Day.ToDateTime(new TimeOnly(h, m)));
    static DayRow Today(System.Collections.Generic.IEnumerable<RawEvent> es,
                        DateOnly? since = null, DateTime? now = null)
        => Aggregator.Build(es, Day, Day, Day, now ?? Now, since ?? Day)[0];

    // --- On / Off / Running -------------------------------------------------

    [Fact]
    public void BootAndShutdown_SetsOnOff()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 34), Ev(EventKind.Shutdown, 18, 42) });
        Assert.Equal(new TimeOnly(8, 34), r.On);
        Assert.Equal(new TimeOnly(18, 42), r.Off);
        Assert.False(r.Running);
    }

    [Fact]
    public void NoBoot_OnIsNull()
    {
        var r = Today(new[] { Ev(EventKind.Shutdown, 18, 0) });
        Assert.Null(r.On);
    }

    [Fact]
    public void BootNoShutdownToday_IsRunning()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0) });
        Assert.True(r.Running);
        Assert.Null(r.Off);
    }

    // --- BarEnd (effective bar end) ----------------------------------------

    [Fact]
    public void BarEnd_FinishedDay_IsOff()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0) });
        Assert.Equal(new TimeOnly(18, 0), r.BarEnd);
    }

    [Fact]
    public void BarEnd_Running_IsNowClock()
    {
        var now = Day.ToDateTime(new TimeOnly(14, 0));
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0) }, now: now);
        Assert.Equal(new TimeOnly(14, 0), r.BarEnd);
    }

    // --- Pauses (lock intervals cut from the uptime span) ------------------

    [Fact]
    public void Pause_LockInsideUptime_OneInterval()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Lock, 12, 3), Ev(EventKind.Unlock, 12, 41) });
        Assert.Single(r.DayLocks);
        Assert.Equal(new TimeOnly(12, 3), r.DayLocks[0].Start);
        Assert.Equal(new TimeOnly(12, 41), r.DayLocks[0].End);
        Assert.Equal(38, r.PauseMinutes);
    }

    [Fact]
    public void Pause_LockBeforeBoot_ClippedToOn()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Lock, 7, 30), Ev(EventKind.Unlock, 8, 30) });
        Assert.Equal(new TimeOnly(8, 0), r.DayLocks[0].Start);
        Assert.Equal(30, r.PauseMinutes);
    }

    [Fact]
    public void Pause_OpenLock_ClampedToBarEnd_AndMarkedOpen()
    {
        var now = Day.ToDateTime(new TimeOnly(12, 0));
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Lock, 9, 0) }, now: now);
        Assert.Single(r.DayLocks);
        Assert.True(r.DayLocks[0].Open);
        Assert.Equal(new TimeOnly(12, 0), r.DayLocks[0].End);   // clamped to now
        Assert.Equal(180, r.PauseMinutes);
    }

    [Fact]
    public void Pause_UnlockWithoutLock_Ignored()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Unlock, 12, 10) });
        Assert.Empty(r.DayLocks);
        Assert.Equal(0, r.PauseMinutes);
        Assert.True(r.HasLockData);
    }

    [Fact]
    public void Pause_MultipleLocks_SummedWholeDay()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Lock, 9, 0), Ev(EventKind.Unlock, 9, 45),     // 45, outside old 11-13 window
            Ev(EventKind.Lock, 12, 10), Ev(EventKind.Unlock, 12, 22) });  // 12
        Assert.Equal(2, r.DayLocks.Count);
        Assert.Equal(57, r.PauseMinutes);   // 45 + 12, whole day not just 11-13
    }

    // --- Work minutes -------------------------------------------------------

    [Fact]
    public void Work_UptimeMinusPause()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Lock, 12, 0), Ev(EventKind.Unlock, 12, 30) });
        Assert.Equal(570, r.WorkMinutes);   // 600 - 30
    }

    [Fact]
    public void Work_ShownWithoutLockData_IsGrossUptime()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0) },
                      since: Day.AddDays(1));   // lock recording starts tomorrow -> no lock data today
        Assert.False(r.HasLockData);
        Assert.Equal(600, r.WorkMinutes);   // shown anyway: gross uptime, no pause subtracted
        Assert.Equal(0, r.PauseMinutes);
        Assert.Empty(r.DayLocks);
    }

    [Fact]
    public void Work_HasLockDataNoLocks_FullUptime()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0) });
        Assert.True(r.HasLockData);
        Assert.Equal(0, r.PauseMinutes);
        Assert.Equal(600, r.WorkMinutes);
        Assert.False(r.PcOff);
    }

    [Fact]
    public void Work_NullWhenNoBoot()
    {
        var r = Today(new[] { Ev(EventKind.Shutdown, 18, 0) });
        Assert.Null(r.WorkMinutes);
        Assert.Null(r.BarEnd);
    }

    [Fact]
    public void Work_PastDayNoShutdown_NullNoBar()
    {
        var past = Day.AddDays(-3);
        var ev = new RawEvent(EventKind.Boot, past.ToDateTime(new TimeOnly(8, 0)));
        var r = Aggregator.Build(new[] { ev }, past, past, Day, Now, Day.AddDays(-10))[0];
        Assert.False(r.Running);
        Assert.Null(r.WorkMinutes);
        Assert.Null(r.BarEnd);
    }

    [Fact]
    public void Work_RunningDay_UsesNowAsEnd()
    {
        var now = Day.ToDateTime(new TimeOnly(12, 0));
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0),
            Ev(EventKind.Lock, 9, 0), Ev(EventKind.Unlock, 9, 15) }, now: now);
        Assert.True(r.Running);
        Assert.Equal(225, r.WorkMinutes);   // (12:00-08:00)=240 - 15
    }

    [Fact]
    public void Work_NeverNegative()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 8, 30),
            Ev(EventKind.Lock, 8, 0), Ev(EventKind.Unlock, 9, 0) });
        Assert.Equal(0, r.WorkMinutes);   // lock clipped to uptime, 30-30
    }

    [Fact]
    public void Work_RunningAfterEarlierShutdown_UsesNow()
    {
        var now = Day.ToDateTime(new TimeOnly(14, 0));
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 9, 0),
            Ev(EventKind.Boot, 10, 0) }, now: now);
        Assert.True(r.Running);
        Assert.Equal(360, r.WorkMinutes);   // 08:00 -> now(14:00) = 360
    }

    [Fact]
    public void NoEvents_PcOff()
    {
        var r = Today(System.Array.Empty<RawEvent>());
        Assert.True(r.PcOff);
        Assert.Null(r.WorkMinutes);
    }

    // --- Date range ---------------------------------------------------------

    [Fact]
    public void Build_ReturnsNewestFirst_AndRequestedCount()
    {
        var rows = Aggregator.Build(System.Array.Empty<RawEvent>(), Day.AddDays(-9), Day, Day, Now, Day);
        Assert.Equal(10, rows.Count);
        Assert.Equal(Day, rows[0].Date);
        Assert.Equal(Day.AddDays(-9), rows[9].Date);
    }

    [Fact]
    public void Build_ArbitraryRange_NotEndingToday()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 5);
        var rows = Aggregator.Build(System.Array.Empty<RawEvent>(), from, to, Day, Now, Day);
        Assert.Equal(5, rows.Count);
        Assert.Equal(to, rows[0].Date);          // newest first
        Assert.Equal(from, rows[4].Date);
        Assert.All(rows, r => Assert.False(r.Running));   // none is today
    }

    [Fact]
    public void Build_RangeIncludingToday_MarksTodayRunning()
    {
        var rows = Aggregator.Build(new[] { Ev(EventKind.Boot, 8, 0) },
            Day.AddDays(-2), Day, Day, Now, Day);
        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].Running);            // rows[0] is today
    }

    [Fact]
    public void Build_FromAfterTo_Empty()
    {
        var rows = Aggregator.Build(System.Array.Empty<RawEvent>(), Day, Day.AddDays(-1), Day, Now, Day);
        Assert.Empty(rows);
    }
}
