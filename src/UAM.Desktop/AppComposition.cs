using UAM.Application;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop;

/// <summary>
/// The composition root: the only place that knows the concrete application
/// services. View models receive interfaces and never see the database.
/// </summary>
internal static class AppComposition
{
    public static MainViewModel CreateMainViewModel(UamApplicationOptions? options = null)
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

        return new MainViewModel(
        [
            new DashboardViewModel(dashboardService),
            new AnalysisViewModel(analysisService),
            new OptimizationViewModel(optimizationService, analysisService),
            new ProfilesViewModel(profileService),
            new HistoryViewModel(historyService),
            new IntegrationsViewModel(),
            new SettingsViewModel(options, dashboardService)
        ]);
    }
}
