using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Overview page. Every value is read from <see cref="IDashboardService"/>,
/// which in turn reads SQLite. Before data exists the page reports an empty
/// state instead of showing placeholder numbers.
/// </summary>
public sealed class DashboardViewModel : PageViewModelBase
{
    private readonly IDashboardService _service;

    private DashboardSummary? _summary;
    private IReadOnlyList<HeatmapCellViewModel> _heatmap = HeatmapCellViewModel.BuildRing(null);
    private IReadOnlyList<SpeedEstimateViewModel> _speedBands = SpeedEstimateViewModel.BuildAll(null);
    private RecommendationItemViewModel? _pendingRecommendation;

    public DashboardViewModel(IDashboardService service)
        : base(PageKey.Dashboard)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsBusy);
        ReviewRecommendationCommand = new RelayCommand(
            () => RequestNavigation(PageKey.Optimization),
            () => HasPendingRecommendation);
        ViewAnalysisCommand = new RelayCommand(
            () => RequestNavigation(PageKey.Analysis),
            () => HasData);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Opens the Optimization page. It never approves anything.</summary>
    public RelayCommand ReviewRecommendationCommand { get; }

    public RelayCommand ViewAnalysisCommand { get; }

    public bool HasData => _summary?.HasData ?? false;

    public bool IsEmpty => HasLoaded && !HasData && !HasError;

    public string SessionCountText => Text(_summary?.SessionCount);

    public string ObservationCountText => Text(_summary?.ObservationCount);

    public string GameCountText => Text(_summary?.GameCount);

    public string ProfileCountText => Text(_summary?.ProfileCount);

    public string PendingApprovalCountText => Text(_summary?.PendingApprovalCount);

    public string LastObservationText => UamFormat.Date(_summary?.LastObservationUtc);

    public IReadOnlyList<HeatmapCellViewModel> Heatmap => _heatmap;

    public IReadOnlyList<SpeedEstimateViewModel> SpeedBands => _speedBands;

    public bool HasPendingRecommendation => _pendingRecommendation is not null;

    public RecommendationItemViewModel? PendingRecommendation => _pendingRecommendation;

    public bool HasActiveProfile => _summary?.ActiveProfile is not null;

    public string ActiveProfileText => _summary?.ActiveProfile is { } profile
        ? $"{profile.GameId} · approved {UamFormat.Date(profile.ApprovedAtUtc)}"
        : "No approved profile yet";

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return LoadLatestAsync(
            token => _service.GetSummaryAsync(token),
            Apply,
            cancellationToken);
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void Apply(DashboardSummary summary)
    {
        _summary = summary;
        _heatmap = HeatmapCellViewModel.BuildRing(summary.DirectionalEstimates);
        _speedBands = SpeedEstimateViewModel.BuildAll(summary.SpeedEstimates);
        _pendingRecommendation = summary.PendingRecommendation is null
            ? null
            : new RecommendationItemViewModel(summary.PendingRecommendation);

        OnPropertyChanged(string.Empty);
        ReviewRecommendationCommand.RaiseCanExecuteChanged();
        ViewAnalysisCommand.RaiseCanExecuteChanged();
    }

    private static string Text(int? value)
    {
        return value is null ? "—" : UamFormat.Count(value.Value);
    }
}
