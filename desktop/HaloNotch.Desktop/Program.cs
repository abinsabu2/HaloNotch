using Avalonia;
using Avalonia.Controls;

namespace HaloNotch;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // One instance per user session (a second launch would add a second notch on every display).
        using var mutex = new Mutex(true, "Local\\HaloNotch.SingleInstance", out bool first);
        // "Relaunch" (language change) starts the new instance before the old one has exited – wait for it.
        if (!first && !(args.Contains("--relaunch") && WaitFor(mutex))) return;
        Loc.ApplyCulture();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    static bool WaitFor(Mutex mutex)
    {
        try { return mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { return true; }       // previous instance exited without releasing
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
