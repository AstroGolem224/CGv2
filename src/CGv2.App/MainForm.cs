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
        try
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
        var html = LoadTemplate()
            .Replace("/*__DATA__*/[]", JsonSerializer.Serialize(BuildRows().Select(WebRow.From)));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cgv2-report.html");
        File.WriteAllText(path, html, new UTF8Encoding(true));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
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
