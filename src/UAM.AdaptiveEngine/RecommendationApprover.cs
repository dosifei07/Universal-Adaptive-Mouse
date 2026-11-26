using UAM.Core.Models;

namespace UAM.AdaptiveEngine;

/// <summary>
/// The explicit boundary between a calculated recommendation and a profile
/// that may be exported or applied. Nothing is approved implicitly.
/// </summary>
public sealed class RecommendationApprover
{
    public GameProfile Approve(
        OptimizationRecommendation recommendation,
        string approvedBy = "local-user")
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);

        if (!recommendation.IsActionable)
        {
            throw new InvalidOperationException(
                "This recommendation is not actionable and cannot be approved: " +
                recommendation.Reason);
        }

        if (!recommendation.RequiresApproval)
        {
            throw new InvalidOperationException(
                "This recommendation does not require approval.");
        }

        return recommendation.ProposedProfile with
        {
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}
