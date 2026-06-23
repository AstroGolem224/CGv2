using System.Drawing;
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
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 680);
        MinimumSize = new Size(560, 360);
        BackColor = Color.FromArgb(14, 17, 22);
        Icon = LoadAppIcon();
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
    }

    private const int CS_DROPSHADOW = 0x20000;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= CS_DROPSHADOW; return cp; }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int round = 2; // DWMWCP_ROUND — leichte Win11-Eckenrundung
            DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, sizeof(int));
        }
        catch { }
    }

    private async Task InitAsync()
    {
        try
        {
            await _web.EnsureCoreWebView2Async();
            _web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                var msg = e.TryGetWebMessageAsString();
                switch (msg)
                {
                    case "min": WindowState = FormWindowState.Minimized; break;
                    case "max": ToggleMax(); break;
                    case "close": Close(); break;
                    case "drag": NcDrag(HTCAPTION); break;
                    case "export": ExportCsv(); break;
                    default:
                        if (msg != null && msg.StartsWith("range:")) ApplyRange(msg);
                        else if (msg != null && msg.StartsWith("resize:")) StartResize(msg);
                        else Render();
                        break;
                }
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

    // --- Custom window chrome (frameless): drag / resize / min / max via native hit-test ---

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;

    private void NcDrag(int hit)
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, hit, 0);
    }

    private void StartResize(string msg)
    {
        int ht = msg switch
        {
            "resize:l" => 10, "resize:r" => 11, "resize:t" => 12, "resize:tl" => 13,
            "resize:tr" => 14, "resize:b" => 15, "resize:bl" => 16, "resize:br" => 17, _ => 0
        };
        if (ht != 0) NcDrag(ht);
    }

    private void ToggleMax()
    {
        if (WindowState == FormWindowState.Maximized)
            WindowState = FormWindowState.Normal;
        else
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
    }

    internal static Icon LoadAppIcon()
    {
        using var s = typeof(MainForm).Assembly.GetManifestResourceStream("CGv2.App.app.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
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
