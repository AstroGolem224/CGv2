using CGv2.Core;
using Xunit;

public class AggregatorTests
{
    static readonly DateOnly Day = new(2026, 6, 22);
    static readonly TimeOnly W1 = new(11, 0), W2 = new(13, 0);
    static readonly DateTime Now = Day.ToDateTime(new TimeOnly(23, 59));

    static RawEvent Ev(EventKind k, int h, int m) => new(k, Day.ToDateTime(new TimeOnly(h, m)));
    static DayRow Today(System.Collections.Generic.IEnumerable<RawEvent> es,
                        DateOnly? since = null, DateTime? now = null)
        => Aggregator.Build(es, Day, now ?? Now, 1, W1, W2, since ?? Day)[0];

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

    [Fact]
    public void LockInsideWindow_OneInterval()
    {
        var r = Today(new[] { Ev(EventKind.Lock, 12, 3), Ev(EventKind.Unlock, 12, 41) });
        Assert.Single(r.WindowLocks);
        Assert.Equal(new TimeOnly(12, 3), r.WindowLocks[0].Start);
        Assert.Equal(new TimeOnly(12, 41), r.WindowLocks[0].End);
        Assert.Equal(38, r.WindowMinutes);
    }

    [Fact]
    public void LockSpanningWholeWindow_ClippedTo120()
    {
        var r = Today(new[] { Ev(EventKind.Lock, 10, 30), Ev(EventKind.Unlock, 13, 30) });
        Assert.Equal(new TimeOnly(11, 0), r.WindowLocks[0].Start);
        Assert.Equal(new TimeOnly(13, 0), r.WindowLocks[0].End);
        Assert.Equal(120, r.WindowMinutes);
    }

    [Fact]
    public void OpenLock_ClampedToWindowEnd_AndMarkedOpen()
    {
        var r = Today(new[] { Ev(EventKind.Lock, 12, 30) });
        Assert.Single(r.WindowLocks);
        Assert.True(r.WindowLocks[0].Open);
        Assert.Equal(new TimeOnly(13, 0), r.WindowLocks[0].End);
        Assert.Equal(30, r.WindowMinutes);
    }

    [Fact]
    public void UnlockWithoutLock_Ignored()
    {
        var r = Today(new[] { Ev(EventKind.Unlock, 12, 10) });
        Assert.Empty(r.WindowLocks);
        Assert.Equal(0, r.WindowMinutes);
        Assert.True(r.HasLockData);
    }

    [Fact]
    public void MultipleLocks_AllListed_SecondClipped()
    {
        var r = Today(new[]
        {
            Ev(EventKind.Lock, 12, 10), Ev(EventKind.Unlock, 12, 22),
            Ev(EventKind.Lock, 12, 55), Ev(EventKind.Unlock, 13, 10)
        });
        Assert.Equal(2, r.WindowLocks.Count);
        Assert.Equal(new TimeOnly(13, 0), r.WindowLocks[1].End);
        Assert.Equal(17, r.WindowMinutes);
    }

    [Fact]
    public void PartialOverlapStart_Clipped()
    {
        var r = Today(new[] { Ev(EventKind.Lock, 10, 50), Ev(EventKind.Unlock, 11, 20) });
        Assert.Equal(new TimeOnly(11, 0), r.WindowLocks[0].Start);
        Assert.Equal(20, r.WindowMinutes);
    }

    [Fact]
    public void BeforeLockDataSince_HasLockDataFalse()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0) }, since: Day.AddDays(1));
        Assert.False(r.HasLockData);
        Assert.Empty(r.WindowLocks);
    }

    [Fact]
    public void HasDataButNoLocks_ZeroMinutes_NotPcOff()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0) });
        Assert.True(r.HasLockData);
        Assert.Equal(0, r.WindowMinutes);
        Assert.False(r.PcOff);
    }

    [Fact]
    public void NoEvents_PcOff()
    {
        var r = Today(System.Array.Empty<RawEvent>());
        Assert.True(r.PcOff);
    }

    [Fact]
    public void Build_ReturnsNewestFirst_AndRequestedCount()
    {
        var rows = Aggregator.Build(System.Array.Empty<RawEvent>(), Day, Now, 10, W1, W2, Day);
        Assert.Equal(10, rows.Count);
        Assert.Equal(Day, rows[0].Date);
        Assert.Equal(Day.AddDays(-9), rows[9].Date);
    }

    [Fact]
    public void Work_UptimeMinusAllDayLocks()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Lock, 12, 0), Ev(EventKind.Unlock, 12, 30) });
        Assert.Equal(570, r.WorkMinutes);   // 600 - 30
    }

    [Fact]
    public void Work_SubtractsLocksOutsideWindowToo()
    {
        var r = Today(new[] {
            Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0),
            Ev(EventKind.Lock, 9, 0), Ev(EventKind.Unlock, 9, 45) });
        Assert.Equal(555, r.WorkMinutes);   // 600 - 45 (lock outside 11-13)
        Assert.Empty(r.WindowLocks);
    }

    [Fact]
    public void Work_NullWhenNoLockData()
    {
        var r = Today(new[] { Ev(EventKind.Boot, 8, 0), Ev(EventKind.Shutdown, 18, 0) },
                      since: Day.AddDays(1));
        Assert.Null(r.WorkMinutes);
    }

    [Fact]
    public void Work_NullWhenNoBoot()
    {
        var r = Today(new[] { Ev(EventKind.Shutdown, 18, 0) });
        Assert.Null(r.WorkMinutes);
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
        Assert.Equal(0, r.WorkMinutes);     // lock clipped to uptime, 30-30
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
}
