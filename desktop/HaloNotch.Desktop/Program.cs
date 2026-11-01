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
        if (!first) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
