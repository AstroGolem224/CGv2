# CGv2 Activity Tracker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Portable, no-admin Windows desktop app that shows Boot/Shutdown and 11–13 h lock activity of the last 10 days as a copyable table, built (cross-compiled) from Linux.

**Architecture:** Three .NET 10 projects. `CGv2.Core` (net10.0, pure logic — Models + Aggregator, unit-tested on Linux). `CGv2.App` (net10.0-windows, WinForms tray host + WebView2 UI + Windows glue: SessionSwitch lock logger, System-EventLog reader, JSON store, HKCU autostart). `CGv2.Core.Tests` (xUnit, runs on Linux). The pure aggregation logic is isolated so it is fully testable without Windows; the Windows-only glue is verified manually on the target per the spec.

**Tech Stack:** .NET 10, WinForms, Microsoft.Web.WebView2, System.Diagnostics.Eventing.Reader, Microsoft.Win32.SystemEvents, xUnit. Frontend = embedded HTML/CSS/JS (the approved `ui-mockup.html`).

**Spec:** `2026-06-22-cgv2-activity-tracker-design.md` · **UI:** `ui-mockup.html` (both in this folder).

---

## File Structure

```
CGv2/                                  (repo root — github.com/AstroGolem224/CGv2)
  CGv2.sln
  .gitignore
  README.md
  docs/                                (copy of spec, plan, mockup)
  src/
    CGv2.Core/
      CGv2.Core.csproj                 net10.0 — pure, no Windows deps
      Models.cs                        RawEvent, EventKind, LockInterval, DayRow
      Aggregator.cs                    events -> DayRow[] (the meat, TDD)
    CGv2.App/
      CGv2.App.csproj                  net10.0-windows, WinForms, WebView2
      app.manifest                     requestedExecutionLevel=asInvoker (no UAC)
      Program.cs                       entry, single-instance mutex
      TrayAgent.cs                     NotifyIcon + menu (Anzeigen/Autostart/Beenden)
      MainForm.cs                      WebView2 host, injects DayRow JSON
      WebRow.cs                        DayRow -> web view-model (timeline %, labels)
      LockLogger.cs                    SessionSwitch -> Store
      EventLogSource.cs                System log Kernel-General 12/13
      Store.cs                         lock-log.json persistence + path resolution
      Autostart.cs                     HKCU\...\Run toggle
      web/index.html                   embedded frontend (from ui-mockup.html)
  tests/
    CGv2.Core.Tests/
      CGv2.Core.Tests.csproj           net10.0, xUnit
      AggregatorTests.cs               edge cases from spec §6
```

**Interface contract (locked here, referenced by every task):**

```csharp
public enum EventKind { Boot, Shutdown, Lock, Unlock }
public readonly record struct RawEvent(EventKind Kind, DateTime TimestampLocal);
public readonly record struct LockInterval(TimeOnly Start, TimeOnly End, bool Open);
public sealed record DayRow(
    DateOnly Date, TimeOnly? On, TimeOnly? Off, bool Running, bool PcOff,
    bool HasLockData, IReadOnlyList<LockInterval> WindowLocks, int WindowMinutes,
    int? WorkMinutes);   // ((Off-On) - all-day locks); null when not computable

public static class Aggregator {
    public static List<DayRow> Build(
        IEnumerable<RawEvent> events, DateOnly today, DateTime now, int days,
        TimeOnly windowStart, TimeOnly windowEnd, DateOnly? lockDataSince);
}
```

---

## Task 1: Repo scaffold + solution

**Files:**
- Create: `CGv2.sln`, `.gitignore`, `README.md`, `docs/` (copy spec/plan/mockup)
- Create project skeletons (filled in later tasks)

- [ ] **Step 1: Clone the empty repo**

```bash
cd ~/Dokumente/Github
gh repo clone AstroGolem224/CGv2
cd CGv2
```

- [ ] **Step 2: Create the three projects + solution**

```bash
dotnet new classlib -n CGv2.Core -o src/CGv2.Core -f net10.0
dotnet new winforms  -n CGv2.App  -o src/CGv2.App  -f net10.0-windows
dotnet new xunit     -n CGv2.Core.Tests -o tests/CGv2.Core.Tests -f net10.0
rm src/CGv2.Core/Class1.cs src/CGv2.Core.Tests/UnitTest1.cs 2>/dev/null; rm tests/CGv2.Core.Tests/UnitTest1.cs 2>/dev/null
dotnet new sln -n CGv2
dotnet sln add src/CGv2.Core/CGv2.Core.csproj src/CGv2.App/CGv2.App.csproj tests/CGv2.Core.Tests/CGv2.Core.Tests.csproj
dotnet add src/CGv2.App/CGv2.App.csproj reference src/CGv2.Core/CGv2.Core.csproj
dotnet add tests/CGv2.Core.Tests/CGv2.Core.Tests.csproj reference src/CGv2.Core/CGv2.Core.csproj
```

- [ ] **Step 3: Write `.gitignore`** (security: never commit personal activity data)

```gitignore
bin/
obj/
*.user
publish/
lock-log.json
lock-log.json.bak
*.bak
.vs/
```

- [ ] **Step 4: Copy design artifacts into the repo**

```bash
mkdir -p docs
cp ~/Dokumente/UMBRA-Notes/DDs/CGv2/2026-06-22-cgv2-activity-tracker-design.md docs/
cp ~/Dokumente/UMBRA-Notes/DDs/CGv2/2026-06-22-cgv2-implementation-plan.md docs/
cp ~/Dokumente/UMBRA-Notes/DDs/CGv2/ui-mockup.html docs/
```

- [ ] **Step 5: Write minimal `README.md`**

```markdown
# CGv2 — Activity Ledger

Portable, no-admin Windows tool. Shows Boot/Shutdown and 11–13 h lock activity
of the last 10 days as a copyable (TSV) table. Runs from the tray, logs lock
events going forward (no admin = no historical locks).

## Build (from Linux or Windows)
    dotnet publish src/CGv2.App/CGv2.App.csproj -c Release -r win-x64 \
      --self-contained true -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true

Output: a single portable `CGv2.exe`. Copy to the Windows PC, run, optionally
enable Autostart from the tray menu. Data (`lock-log.json`) is stored next to
the exe; never committed.

## Test (Linux)
    dotnet test
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, three projects, docs, gitignore"
```

---

## Task 2: Core models

**Files:**
- Create: `src/CGv2.Core/Models.cs`
- Modify: `src/CGv2.Core/CGv2.Core.csproj`

- [ ] **Step 1: Set Core csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Write `Models.cs`**

```csharp
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
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/CGv2.Core/CGv2.Core.csproj`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/CGv2.Core
git commit -m "feat(core): add domain models"
```

---

## Task 3: Aggregator (TDD — the core logic)

**Files:**
- Test: `tests/CGv2.Core.Tests/AggregatorTests.cs`
- Create: `src/CGv2.Core/Aggregator.cs`

- [ ] **Step 1: Write the failing tests** (covers spec §6 edge cases)

```csharp
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
}
```

- [ ] **Step 2: Run tests to confirm they fail**

Run: `dotnet test tests/CGv2.Core.Tests/CGv2.Core.Tests.csproj`
Expected: FAIL — `Aggregator` does not exist / compile error.

- [ ] **Step 3: Implement `Aggregator.cs`**

```csharp
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
```

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test tests/CGv2.Core.Tests/CGv2.Core.Tests.csproj`
Expected: PASS — all 19 tests green (13 boot/lock + 6 worktime).

- [ ] **Step 5: Commit**

```bash
git add src/CGv2.Core/Aggregator.cs tests/CGv2.Core.Tests/AggregatorTests.cs
git commit -m "feat(core): aggregator with 11-13h lock windowing + tests"
```

---

## Task 4: App csproj + manifest (no-admin, cross-buildable)

**Files:**
- Modify: `src/CGv2.App/CGv2.App.csproj`
- Create: `src/CGv2.App/app.manifest`

- [ ] **Step 1: Write `CGv2.App.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>CGv2</AssemblyName>
    <RootNamespace>CGv2.App</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <SelfContained>true</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2592.51" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CGv2.Core\CGv2.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="web\index.html" />
  </ItemGroup>
</Project>
```

`EnableWindowsTargeting` is what lets `net10.0-windows` build on Linux.

- [ ] **Step 2: Write `app.manifest`** (asInvoker = never triggers a UAC/admin prompt)

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 3: Remove the default `Form1`** generated by the template

```bash
rm -f src/CGv2.App/Form1.cs src/CGv2.App/Form1.Designer.cs src/CGv2.App/Form1.resx
```

Leave `Program.cs` for Task 9 to overwrite. App won't build yet (no web/index.html, no Program). That's expected — next tasks fill it.

- [ ] **Step 4: Commit**

```bash
git add src/CGv2.App/CGv2.App.csproj src/CGv2.App/app.manifest
git commit -m "build(app): winforms+webview2 csproj, asInvoker manifest, linux cross-build"
```

---

## Task 5: Store (JSON persistence + portable path)

**Files:**
- Create: `src/CGv2.App/Store.cs`

- [ ] **Step 1: Write `Store.cs`**

```csharp
using System.Text.Json;
using CGv2.Core;

namespace CGv2.App;

public sealed class Store
{
    private readonly string _path;
    public string Path => _path;

    public Store()
    {
        var baseDir = AppContext.BaseDirectory;
        var probe = System.IO.Path.Combine(baseDir, ".cgv2-write-test");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            _path = System.IO.Path.Combine(baseDir, "lock-log.json");
        }
        catch
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CGv2");
            Directory.CreateDirectory(dir);
            _path = System.IO.Path.Combine(dir, "lock-log.json");
        }
    }

    private readonly record struct StoredLock(EventKind Kind, DateTime Ts);

    public List<RawEvent> Load()
        => LoadStored().Select(s => new RawEvent(s.Kind, s.Ts)).ToList();

    public DateOnly? FirstLockDate()
    {
        var all = LoadStored();
        return all.Count == 0 ? null : DateOnly.FromDateTime(all.Min(s => s.Ts));
    }

    public void Append(EventKind kind, DateTime ts)
    {
        var list = LoadStored();
        if (list.Any(s => s.Kind == kind && s.Ts == ts)) return;
        list.Add(new StoredLock(kind, ts));
        File.WriteAllText(_path, JsonSerializer.Serialize(list));
    }

    private List<StoredLock> LoadStored()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<StoredLock>>(File.ReadAllText(_path)) ?? new();
        }
        catch
        {
            try { File.Copy(_path, _path + ".bak", true); } catch { }
            return new();
        }
    }
}
```

- [ ] **Step 2: Verify it compiles** (Core reference resolves)

Run: `dotnet build src/CGv2.App/CGv2.App.csproj`
Expected: compile errors only about missing `Program`/`web` (Store itself compiles). If Store has its own errors, fix before continuing.

- [ ] **Step 3: Commit**

```bash
git add src/CGv2.App/Store.cs
git commit -m "feat(app): portable json store for lock events"
```

---

## Task 6: EventLogSource (System log boot/shutdown)

**Files:**
- Create: `src/CGv2.App/EventLogSource.cs`

- [ ] **Step 1: Write `EventLogSource.cs`**

```csharp
using System.Diagnostics.Eventing.Reader;
using CGv2.Core;

namespace CGv2.App;

public static class EventLogSource
{
    public static List<RawEvent> ReadBootShutdown(int days)
    {
        var result = new List<RawEvent>();
        long ms = (long)TimeSpan.FromDays(days + 1).TotalMilliseconds;
        string xpath =
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] " +
            "and (EventID=12 or EventID=13) " +
            $"and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);
            for (EventRecord? rec = reader.ReadEvent(); rec != null; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    if (rec.TimeCreated is not DateTime t || rec.Id is not (12 or 13)) continue;
                    var kind = rec.Id == 12 ? EventKind.Boot : EventKind.Shutdown;
                    result.Add(new RawEvent(kind, t));
                }
            }
        }
        catch (EventLogException) { }
        return result;
    }
}
```

`EventRecord.TimeCreated` is already local time; no conversion. Reading the
System log needs no admin. Failures degrade to an empty list (spec §9).

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/CGv2.App/CGv2.App.csproj`
Expected: only `Program`/`web` errors remain.

- [ ] **Step 3: Commit**

```bash
git add src/CGv2.App/EventLogSource.cs
git commit -m "feat(app): read boot/shutdown from system event log"
```

---

## Task 7: LockLogger + Autostart

**Files:**
- Create: `src/CGv2.App/LockLogger.cs`, `src/CGv2.App/Autostart.cs`

- [ ] **Step 1: Write `LockLogger.cs`**

```csharp
using Microsoft.Win32;
using CGv2.Core;

namespace CGv2.App;

public sealed class LockLogger : IDisposable
{
    private readonly Store _store;

    public LockLogger(Store store)
    {
        _store = store;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            _store.Append(EventKind.Lock, DateTime.Now);
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
            _store.Append(EventKind.Unlock, DateTime.Now);
    }

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
```

- [ ] **Step 2: Write `Autostart.cs`**

```csharp
using Microsoft.Win32;

namespace CGv2.App;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CGv2";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string v && v.Trim('"') == ExePath();
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                      ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k!.SetValue(ValueName, $"\"{ExePath()}\"");
        else k!.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string ExePath() => Environment.ProcessPath ?? "";
}
```

> ponytail: HKCU Run key instead of a Startup-folder `.lnk` — same effect (no
> admin, opt-in, removable), no COM/shortcut plumbing. If the exe is moved, the
> path goes stale; re-toggle. Upgrade path: switch to a Startup `.lnk` via
> WScript.Shell COM if a relocatable shortcut is ever needed.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/CGv2.App/CGv2.App.csproj`
Expected: only `Program`/`web` errors remain.

- [ ] **Step 4: Commit**

```bash
git add src/CGv2.App/LockLogger.cs src/CGv2.App/Autostart.cs
git commit -m "feat(app): session lock logger + hkcu autostart toggle"
```

---

## Task 8: Frontend (embedded web UI)

**Files:**
- Create: `src/CGv2.App/web/index.html` (from `docs/ui-mockup.html`)

- [ ] **Step 1: Copy the approved mockup as the app frontend**

```bash
mkdir -p src/CGv2.App/web
cp docs/ui-mockup.html src/CGv2.App/web/index.html
```

- [ ] **Step 2: Replace the hardcoded demo data with an injection placeholder**

In `src/CGv2.App/web/index.html`, find the line beginning `const D=[` and the
matching closing `];` (the demo array) and replace the **entire array literal**
with the placeholder so the line reads exactly:

```javascript
const D=/*__DATA__*/[];
```

(`MainForm` replaces `/*__DATA__*/[]` with the real JSON at runtime.)

- [ ] **Step 3: Wire the refresh button to call back into C#**

In the same file, immediately **after** the existing
`document.getElementById('cg-copyall').addEventListener(...)` block and before
`</script>`, add:

```javascript
const rf=document.getElementById('cg-refresh');
if(rf)rf.addEventListener('click',()=>{ if(window.chrome&&window.chrome.webview) window.chrome.webview.postMessage('refresh'); });
```

- [ ] **Step 4: Verify the frontend renders standalone (Linux design check)**

```bash
xdg-open docs/ui-mockup.html
```

Confirm: dark console layout, 10 rows, green **Arbeit (h)** column, amber
timeline blocks, dimmed no-data rows, copy buttons, **CSV** button (standalone
falls back to a blob download; in-app it posts `export` to C#). (The
`web/index.html` differs from the mockup only by the data placeholder.)

- [ ] **Step 5: Commit**

```bash
git add src/CGv2.App/web/index.html
git commit -m "feat(app): embed frontend with data placeholder + refresh bridge"
```

---

## Task 9: WebRow mapper + MainForm (WebView2 host)

**Files:**
- Create: `src/CGv2.App/WebRow.cs`, `src/CGv2.App/MainForm.cs`

- [ ] **Step 1: Write `WebRow.cs`** (DayRow → the JS view-model shape)

```csharp
using System.Globalization;
using System.Text;
using CGv2.Core;

namespace CGv2.App;

public static class WebRow
{
    private const double WindowMinutes = 120.0;   // 11:00–13:00
    private const double WindowStartMin = 660.0;   // 11:00 in minutes
    internal static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static object From(DayRow r)
    {
        var blk = r.WindowLocks.Select(l =>
        {
            double startMin = l.Start.ToTimeSpan().TotalMinutes - WindowStartMin;
            double dur = (l.End.ToTimeSpan() - l.Start.ToTimeSpan()).TotalMinutes;
            return new[]
            {
                Math.Round(startMin / WindowMinutes * 100, 1),
                Math.Round(dur / WindowMinutes * 100, 1)
            };
        }).ToArray();

        var t = r.WindowLocks
            .Select(l => $"{l.Start:HH\\:mm}–{l.End:HH\\:mm}" + (l.Open ? " →" : ""))
            .ToArray();

        return new
        {
            d = r.Date.ToString("dd.MM."),
            wd = Weekday(r.Date.DayOfWeek),
            on = r.On?.ToString("HH\\:mm"),
            off = r.Running ? "läuft" : r.Off?.ToString("HH\\:mm"),
            run = r.Running,
            pcoff = r.PcOff ? 1 : 0,
            blk,
            t,
            sum = r.WindowMinutes.ToString(),
            work = (r.HasLockData && r.WorkMinutes is int wm) ? (wm / 60.0).ToString("0.0", De) : null,
            data = r.HasLockData ? 1 : 0,
            note = (r.HasLockData && r.WindowLocks.Count == 0) ? "durchgehend aktiv" : ""
        };
    }

    internal static string Weekday(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Di",
        DayOfWeek.Wednesday => "Mi",
        DayOfWeek.Thursday => "Do",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        _ => "So"
    };
}

public static class CsvBuilder
{
    public static string Build(IEnumerable<DayRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Datum;An;Aus;Arbeit (h);Gesperrt 11-13;Minuten\r\n");
        foreach (var r in rows)
        {
            string on = r.On?.ToString("HH\\:mm") ?? "";
            string off = r.Running ? "läuft" : r.PcOff ? "aus" : (r.Off?.ToString("HH\\:mm") ?? "");
            string work = (r.HasLockData && r.WorkMinutes is int wm)
                ? (wm / 60.0).ToString("0.0", WebRow.De) : "";
            string locks = !r.HasLockData ? ""
                : r.WindowLocks.Count == 0 ? "00:00"
                : string.Join(" ", r.WindowLocks.Select(l => $"{l.Start:HH\\:mm}-{l.End:HH\\:mm}"));
            string min = r.HasLockData ? r.WindowMinutes.ToString() : "";
            sb.Append($"{WebRow.Weekday(r.Date.DayOfWeek)} {r.Date:dd.MM.};{on};{off};{work};{locks};{min}\r\n");
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 2: Write `MainForm.cs`**

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using CGv2.Core;

namespace CGv2.App;

public sealed class MainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Store _store;

    public MainForm(Store store)
    {
        _store = store;
        Text = "CGv2 — Activity Ledger";
        Width = 840;
        Height = 660;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.FromArgb(14, 17, 22);
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        await _web.EnsureCoreWebView2Async();
        _web.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            var msg = e.TryGetWebMessageAsString();
            if (msg == "export") ExportCsv();
            else Render();
        };
        Render();
    }

    private List<DayRow> BuildRows()
    {
        var locks = _store.Load();
        var boot = EventLogSource.ReadBootShutdown(10);
        return Aggregator.Build(
            locks.Concat(boot),
            DateOnly.FromDateTime(DateTime.Now),
            DateTime.Now,
            10,
            new TimeOnly(11, 0), new TimeOnly(13, 0),
            _store.FirstLockDate());
    }

    private void Render()
    {
        var json = JsonSerializer.Serialize(BuildRows().Select(WebRow.From));
        var html = LoadTemplate().Replace("/*__DATA__*/[]", json);
        _web.NavigateToString(html);
    }

    private void ExportCsv()
    {
        using var dlg = new SaveFileDialog
        {
            FileName = "cgv2-arbeitszeiten.csv",
            Filter = "CSV (Semikolon)|*.csv"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dlg.FileName, CsvBuilder.Build(BuildRows()), new UTF8Encoding(true));
    }

    private static string LoadTemplate()
    {
        var asm = typeof(MainForm).Assembly;
        using var s = asm.GetManifestResourceStream("CGv2.App.web.index.html")
                      ?? throw new InvalidOperationException("embedded web/index.html missing");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
```

`NavigateToString` runs on the UI thread; `ExportCsv` opens a native
`SaveFileDialog` and writes UTF-8 **with BOM** so Excel/LibreOffice read the
`;`-separated, comma-decimal CSV correctly.

The embedded-resource name `CGv2.App.web.index.html` = RootNamespace + path.
`NavigateToString` handles our small (<2 MB) document. If the WebView2 runtime
is absent, `EnsureCoreWebView2Async` throws — handled in Task 10 fallback.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/CGv2.App/CGv2.App.csproj`
Expected: only the missing-`Program` error remains.

- [ ] **Step 4: Commit**

```bash
git add src/CGv2.App/WebRow.cs src/CGv2.App/MainForm.cs
git commit -m "feat(app): webview2 host + dayrow->view-model mapping"
```

---

## Task 10: Program + TrayAgent (entry, single instance, tray, WebView2 fallback)

**Files:**
- Create: `src/CGv2.App/TrayAgent.cs`
- Overwrite: `src/CGv2.App/Program.cs`

- [ ] **Step 1: Write `TrayAgent.cs`**

```csharp
namespace CGv2.App;

public sealed class TrayAgent : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Store _store;
    private MainForm? _form;

    public TrayAgent(Store store)
    {
        _store = store;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Anzeigen", null, (_, _) => Show());

        var auto = new ToolStripMenuItem("Autostart")
        {
            Checked = Autostart.IsEnabled(),
            CheckOnClick = true
        };
        auto.CheckedChanged += (_, _) => Autostart.Set(auto.Checked);
        menu.Items.Add(auto);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Application.Exit());

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "CGv2 — Activity Ledger",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => Show();
    }

    private void Show()
    {
        if (_form is null || _form.IsDisposed)
        {
            try
            {
                _form = new MainForm(_store);
                _form.FormClosed += (_, _) => _form = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2-Runtime fehlt vermutlich.\n\n" +
                    "Installiere die 'Evergreen WebView2 Runtime' oder öffne den Report im Browser.\n\n" +
                    ex.Message,
                    "CGv2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        _form.Show();
        _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

- [ ] **Step 2: Overwrite `Program.cs`**

```csharp
namespace CGv2.App;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new System.Threading.Mutex(true, "CGv2_SingleInstance_8f3a", out bool isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        var store = new Store();
        using var logger = new LockLogger(store);
        using var tray = new TrayAgent(store);
        Application.Run();
    }
}
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build`
Expected: `Build succeeded` for all three projects.

- [ ] **Step 4: Run the Core tests once more (regression)**

Run: `dotnet test`
Expected: PASS — 19 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CGv2.App/TrayAgent.cs src/CGv2.App/Program.cs
git commit -m "feat(app): tray agent, single-instance entry, webview2-missing fallback"
```

---

## Task 11: Publish portable EXE + manual Windows verification

**Files:** none (build + verify)

- [ ] **Step 1: Publish the single-file portable exe (from Linux)**

Run:
```bash
dotnet publish src/CGv2.App/CGv2.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```
Expected: `CGv2.exe` in `src/CGv2.App/bin/Release/net10.0-windows/win-x64/publish/`.

- [ ] **Step 2: Copy `CGv2.exe` to the Windows PC and run it**

Expected: no UAC prompt (asInvoker), tray icon appears, no admin needed.

- [ ] **Step 3: Verify lock logging** (spec §11)

On Windows: press `Win+L`, unlock. Open `lock-log.json` next to the exe.
Expected: a `Lock` and an `Unlock` entry with timestamps.

- [ ] **Step 4: Verify the table**

Tray → Anzeigen. Expected: window opens, last 10 days listed, real
Boot/Shutdown times, **Arbeit (h)** = `(Aus−An)−Sperrzeit` for days with lock
data (else `—`), today’s lock interval visible if it was in 11–13 h, older days
dimmed (`—`). Click "Kopieren (TSV)", paste into a text editor — expected:
tab-separated rows incl. the work-hours column.

- [ ] **Step 4b: Verify CSV export**

Click "CSV", pick a path. Open the `.csv` in LibreOffice/Excel — expected:
`;`-separated columns, German decimal comma in Arbeit (e.g. `8,5`), all 10 days.

- [ ] **Step 5: Verify autostart**

Tray → Autostart (check). Re-login (or check `regedit`
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `CGv2`). Expected: value
present and the agent runs at login. Uncheck → value removed.

- [ ] **Step 6: Tag the working build + push**

```bash
git tag v0.1.0
git push origin main --tags
```

---

## Self-review notes (filled by plan author)

- **Spec coverage:** §4 architecture → Tasks 1–10. §5.1 boot/shutdown → Task 6. §5.2 lock + auto-save-per-event → Task 7 (`Store.Append` writes immediately, T5). §6 aggregation incl. **Arbeitszeit** → Task 3 (19 tests). §7 UI incl. Arbeit column + **CSV export** → Tasks 8–9 (`CsvBuilder` + `MainForm.ExportCsv`). §8 autostart → Task 7. §9 errors → Store fallback (T5), EventLog catch (T6), WebView2 fallback (T10). §10 publish → Task 11. §11 verification → Tasks 3 (auto) + 11 (manual, incl. CSV). §14 privacy → `.gitignore` (T1).
- **Type consistency:** `Aggregator.Build(events, today, now, days, …)`, `DayRow` (incl. `WorkMinutes`), `LockInterval`, `RawEvent`, `EventKind` identical across Tasks 2/3/5/6/7/9. `Store.Load/Append/FirstLockDate`, `EventLogSource.ReadBootShutdown`, `WebRow.From`/`WebRow.Weekday`/`WebRow.De`, `CsvBuilder.Build`, `Autostart.IsEnabled/Set` referenced consistently. Web view-model field `work` consumed by `web/index.html` `workCell`.
- **Known external dependency:** WebView2 Runtime on target (fallback message in T10). Google Fonts / Tabler CDN in the embedded HTML degrade gracefully offline (mono fallback, blank icons) — layout intact.
