using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using static HaloNotch.Loc;

namespace HaloNotch;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var quit = new NativeMenuItem(L("Quit HaloNotch"));
            quit.Click += (_, _) => desktop.Shutdown();
            TrayIcon.SetIcons(this, new TrayIcons
            {
                new TrayIcon
                {
                    Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://HaloNotch/Assets/AppIcon.ico"))),
                    ToolTipText = "HaloNotch",
                    Menu = new NativeMenu { quit },
                },
            });
            new NotchWindow().Show();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
