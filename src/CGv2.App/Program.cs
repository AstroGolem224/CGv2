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
        tray.Show();
        Application.Run();
    }
}
