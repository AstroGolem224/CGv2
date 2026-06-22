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

    public void Show()
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
