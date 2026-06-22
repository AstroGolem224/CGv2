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
