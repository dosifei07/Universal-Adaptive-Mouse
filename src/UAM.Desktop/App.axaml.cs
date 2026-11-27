using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UAM.Desktop.ViewModels;
using UAM.Desktop.Views;

namespace UAM.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = AppComposition.CreateMainViewModel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = main,
            };

            // NavigateAsync reports failures through the view model, so this
            // fire-and-forget call cannot fault unobserved.
            _ = main.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}