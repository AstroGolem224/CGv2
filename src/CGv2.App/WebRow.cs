using System.Globalization;
using System.Text;
using CGv2.Core;

namespace CGv2.App;

public static class WebRow
{
    internal static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static int? Minutes(TimeOnly? t) =>
        t.HasValue ? (int)Math.Round(t.Value.ToTimeSpan().TotalMinutes) : null;

    public static object From(DayRow r)
    {
        var gaps = r.DayLocks.Select(l => new[]
        {
            (int)Math.Round(l.Start.ToTimeSpan().TotalMinutes),
            (int)Math.Round(l.End.ToTimeSpan().TotalMinutes)
        }).ToArray();

        return new
        {
            d = r.Date.ToString("dd.MM."),
            wd = Weekday(r.Date.DayOfWeek),
            on = r.On?.ToString("HH\\:mm"),
            off = r.Running ? "läuft" : r.Off?.ToString("HH\\:mm"),
            run = r.Running,
            pcoff = r.PcOff ? 1 : 0,
            bs = Minutes(r.On),          // bar start = On (minutes of day)
            be = Minutes(r.BarEnd),      // bar end = Off or now (minutes of day)
            gaps,                        // pause intervals [startMin, endMin] cut from the bar
            pause = r.HasLockData ? r.PauseMinutes.ToString() : null,
            work = r.WorkMinutes is int wm ? (wm / 60.0).ToString("0.0", De) : null,
            data = r.HasLockData ? 1 : 0
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
        sb.Append("Datum;An;Aus;Arbeit (h);Pause (min)\r\n");
        foreach (var r in rows)
        {
            string on = r.On?.ToString("HH\\:mm") ?? "";
            string off = r.Running ? "läuft" : r.PcOff ? "aus" : (r.Off?.ToString("HH\\:mm") ?? "");
            string work = r.WorkMinutes is int wm ? (wm / 60.0).ToString("0.0", WebRow.De) : "";
            string pause = r.HasLockData ? r.PauseMinutes.ToString() : "";
            sb.Append($"{WebRow.Weekday(r.Date.DayOfWeek)} {r.Date:dd.MM.};{on};{off};{work};{pause}\r\n");
        }
        return sb.ToString();
    }
}
