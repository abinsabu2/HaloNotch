using Microsoft.Win32;

namespace HaloNotch.Win;

/// "Start with Windows": per-user Run key (no admin rights, no scheduled task).
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("HaloNotch") != null;
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("HaloNotch", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("HaloNotch", throwOnMissingValue: false);
        }
    }
}
