namespace UAM.Core.Models;

/// <summary>
/// A recommendation together with the user-facing decision state that is
/// persisted locally. The engine never mutates a stored recommendation; the
/// application layer records decisions through the store.
/// </summary>
public sealed record StoredRecommendation
{
    public required string Id { get; init; }

    public required OptimizationRecommendation Recommendation { get; init; }

    public RecommendationStatus Status { get; init; } = RecommendationStatus.Pending;

    public DateTime StoredAtUtc { get; init; }

    public DateTime? DecidedAtUtc { get; init; }

    public string? DecisionNote { get; init; }

    public string GameId => Recommendation.GameId;

    public bool CanApprove =>
        Status == RecommendationStatus.Pending &&
        Recommendation.IsActionable &&
        Recommendation.RequiresApproval;
}
