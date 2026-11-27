using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UAM.Desktop.Views;

namespace UAM.Desktop;

public partial class App : Avalonia.Application
{
    private DesktopComposition? _composition;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _composition = AppComposition.Create();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _composition.Main,
            };

            // The watcher imports documents dropped into the imports folder.
            // Re-scans are idempotent, so pick up existing files on startup.
            _composition.Watcher?.Start();

            // NavigateAsync reports failures through the view model, so this
            // fire-and-forget call cannot fault unobserved.
            _ = _composition.Main.InitializeAsync();

            desktop.Exit += (_, _) =>
            {
                _ = _composition?.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
