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
