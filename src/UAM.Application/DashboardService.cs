using UAM.Analytics;
using UAM.Core.Models;

namespace UAM.Application;

public sealed record DashboardSummary
{
    public int SessionCount { get; init; }

    public int ObservationCount { get; init; }

    public int GameCount { get; init; }

    public int ProfileCount { get; init; }

    public int PendingApprovalCount { get; init; }

    public DateTime? LastObservationUtc { get; init; }

    public required IReadOnlyList<DirectionalEstimate> DirectionalEstimates { get; init; }

    public required IReadOnlyList<SpeedEstimate> SpeedEstimates { get; init; }

    public StoredRecommendation? PendingRecommendation { get; init; }

    public ApprovedProfile? ActiveProfile { get; init; }

    public bool HasData => ObservationCount > 0;
}

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the dashboard from persisted observations and recommendation state.
/// Every displayed number comes from SQLite; nothing is synthesized.
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private readonly TrainingStoreFactory _storeFactory;
    private readonly UamApplicationOptions _options;
    private readonly PerformanceAnalyzer _analyzer = new();

    public DashboardService(
        TrainingStoreFactory storeFactory,
        UamApplicationOptions options)
    {
        _storeFactory = storeFactory;
        _options = options;
    }

    public async Task<DashboardSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var counts = await store.GetDataCountsAsync(cancellationToken);
        var observations = await store.LoadObservationsAsync(
            cancellationToken: cancellationToken);
        var model = _analyzer.Analyze(_options.PlayerId, observations);

        var pending = await store.GetRecommendationsAsync(
            RecommendationStatus.Pending,
            cancellationToken: cancellationToken);

        var profiles = await store.GetApprovedProfilesAsync(cancellationToken);

        return new DashboardSummary
        {
            SessionCount = counts.SessionCount,
            ObservationCount = counts.ObservationCount,
            GameCount = counts.GameCount,
            ProfileCount = counts.ApprovedProfileCount,
            PendingApprovalCount = counts.PendingRecommendationCount,
            LastObservationUtc = counts.LastObservationUtc,
            DirectionalEstimates = model.DirectionalEstimates,
            SpeedEstimates = model.SpeedEstimates,
            PendingRecommendation = pending.FirstOrDefault(),
            ActiveProfile = profiles.FirstOrDefault()
        };
    }
}
