using UAM.Application;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop;

/// <summary>
/// The composition root: the only place that knows the concrete application
/// services. View models receive interfaces and never see the database.
/// </summary>
internal static class AppComposition
{
    public static DesktopComposition Create(UamApplicationOptions? options = null)
    {
        options ??= new UamApplicationOptions();

        var storeFactory = new TrainingStoreFactory(options.DatabasePath);

        var analysisService = new AnalysisService(storeFactory, options);
        var dashboardService = new DashboardService(storeFactory, options);
        var historyService = new HistoryService(storeFactory);
        var optimizationService = new OptimizationService(
            storeFactory,
            analysisService,
            options);
        var profileService = new ProfileService(storeFactory, options);
        var importService = new ImportService(storeFactory);
        var healthService = new ProviderHealthService(storeFactory);
        var labService = new LabSessionService(importService);

        var watcher = options.WatchImports
            ? new WatchedFolderImportService(importService, options.ImportFolderPath)
            : null;

        var main = new MainViewModel(
        [
            new DashboardViewModel(dashboardService),
            new AnalysisViewModel(analysisService),
            new OptimizationViewModel(optimizationService, analysisService),
            new ProfilesViewModel(profileService),
            new HistoryViewModel(historyService),
            new IntegrationsViewModel(labService, healthService),
            new SettingsViewModel(options, dashboardService)
        ]);

        return new DesktopComposition(main, watcher);
    }

    public static MainViewModel CreateMainViewModel(
        UamApplicationOptions? options = null)
    {
        return Create(options).Main;
    }
}

internal sealed record DesktopComposition(
    MainViewModel Main,
    WatchedFolderImportService? Watcher) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        if (Watcher is not null)
        {
            await Watcher.DisposeAsync();
        }
    }
}
