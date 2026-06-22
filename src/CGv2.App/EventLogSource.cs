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
