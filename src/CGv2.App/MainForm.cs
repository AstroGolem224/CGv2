using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using CGv2.Core;

namespace CGv2.App;

public sealed class MainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Store _store;
    private DateOnly _to = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _from = DateOnly.FromDateTime(DateTime.Now).AddDays(-9);

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
        try
        {
            await _web.EnsureCoreWebView2Async();
            _web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg == "export") ExportCsv();
                else if (msg != null && msg.StartsWith("range:")) ApplyRange(msg);
                else Render();
            };
            Render();
        }
        catch (Exception ex)
        {
            var choice = MessageBox.Show(
                "Die WebView2-Runtime fehlt vermutlich.\n\n" +
                "Soll der Report stattdessen im Standardbrowser geöffnet werden?\n\n" +
                ex.Message,
                "CGv2", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice == DialogResult.Yes) OpenInBrowser();
            Close();
        }
    }

    private void OpenInBrowser()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgv2-report.html");
        File.WriteAllText(path, BuildHtml(), new UTF8Encoding(true));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ApplyRange(string msg)
    {
        var parts = msg.Split(':');
        if (parts.Length == 3
            && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var f)
            && DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t))
        {
            if (f > t) (f, t) = (t, f);
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (t > today) t = today;
            if (t.DayNumber - f.DayNumber > 366) f = t.AddDays(-366);
            _from = f;
            _to = t;
        }
        Render();
    }

    private List<DayRow> BuildRows()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        int daysBack = Math.Max(1, today.DayNumber - _from.DayNumber + 1);
        var locks = _store.Load();
        var boot = EventLogSource.ReadBootShutdown(daysBack);
        return Aggregator.Build(
            locks.Concat(boot),
            _from, _to, today, DateTime.Now,
            new TimeOnly(11, 0), new TimeOnly(13, 0),
            _store.FirstLockDate());
    }

    private string BuildHtml()
    {
        var data = JsonSerializer.Serialize(BuildRows().Select(WebRow.From));
        var range = JsonSerializer.Serialize(new
        {
            from = _from.ToString("yyyy-MM-dd"),
            to = _to.ToString("yyyy-MM-dd"),
            today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd")
        });
        return LoadTemplate()
            .Replace("/*__DATA__*/[]", data)
            .Replace("/*__RANGE__*/null", range);
    }

    private void Render() => _web.NavigateToString(BuildHtml());

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
