using System.Text.Json;
using CGv2.Core;

namespace CGv2.App;

public sealed class Store
{
    private readonly string _path;
    private readonly object _gate = new();
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
    {
        lock (_gate) return LoadStored().Select(s => new RawEvent(s.Kind, s.Ts)).ToList();
    }

    public DateOnly? FirstLockDate()
    {
        lock (_gate)
        {
            var all = LoadStored();
            return all.Count == 0 ? null : DateOnly.FromDateTime(all.Min(s => s.Ts));
        }
    }

    public void Append(EventKind kind, DateTime ts)
    {
        lock (_gate)
        {
            var list = LoadStored();
            if (list.Any(s => s.Kind == kind && s.Ts == ts)) return;
            list.Add(new StoredLock(kind, ts));
            try { File.WriteAllText(_path, JsonSerializer.Serialize(list)); }
            catch { }
        }
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
