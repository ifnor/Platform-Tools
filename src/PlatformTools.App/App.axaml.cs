using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PlatformTools.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            var shareFile = desktop.Args?.Length > 0 && string.Equals(System.IO.Path.GetExtension(desktop.Args[0]), ".ptlink", System.StringComparison.OrdinalIgnoreCase) ? desktop.Args[0] : null;
            if (shareFile is not null) window.Opened += async (_, _) => await window.OpenShareFileAsync(shareFile);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
