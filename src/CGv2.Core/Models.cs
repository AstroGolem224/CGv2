namespace CGv2.Core;

public enum EventKind { Boot, Shutdown, Lock, Unlock }

public readonly record struct RawEvent(EventKind Kind, DateTime TimestampLocal);

public readonly record struct LockInterval(TimeOnly Start, TimeOnly End, bool Open);

public sealed record DayRow(
    DateOnly Date,
    TimeOnly? On,
    TimeOnly? Off,
    bool Running,
    bool PcOff,
    bool HasLockData,
    IReadOnlyList<LockInterval> WindowLocks,
    int WindowMinutes,
    int? WorkMinutes);
