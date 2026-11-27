using UAM.Application;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Read-only view of the application configuration plus a live check that the
/// local database can be opened. Counts come from the dashboard service, so
/// the view model never touches the database itself.
/// </summary>
public sealed class SettingsViewModel : PageViewModelBase
{
    private readonly UamApplicationOptions _options;
    private readonly IDashboardService _dashboardService;

    private DashboardSummary? _summary;

    public SettingsViewModel(
        UamApplicationOptions options,
        IDashboardService dashboardService)
        : base(PageKey.Settings)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dashboardService = dashboardService
            ?? throw new ArgumentNullException(nameof(dashboardService));

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsBusy);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Absolute path of the local SQLite database.</summary>
    public string DatabasePath => Path.GetFullPath(_options.DatabasePath);

    public string PlayerId => _options.PlayerId;

    public string ApprovedBy => _options.ApprovedBy;

    public bool IsDatabaseReachable => _summary is not null;

    public string DatabaseStatus => _summary is not null
        ? "Connected"
        : HasError
            ? "Unavailable"
            : HasLoaded ? "Unknown" : "Checking…";

    public string SessionCountText => Count(_summary?.SessionCount);

    public string ObservationCountText => Count(_summary?.ObservationCount);

    public string GameCountText => Count(_summary?.GameCount);

    public string ProfileCountText => Count(_summary?.ProfileCount);

    public string PendingApprovalCountText => Count(_summary?.PendingApprovalCount);

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var applied = await LoadLatestAsync(
            token => _dashboardService.GetSummaryAsync(token),
            summary => _summary = summary,
            cancellationToken);

        if (!applied && HasError)
        {
            // A failed check must not leave stale counts that look current.
            _summary = null;
        }

        OnPropertyChanged(string.Empty);
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private static string Count(int? value)
    {
        return value is null ? "—" : UamFormat.Count(value.Value);
    }
}
