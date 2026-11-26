using UAM.AdaptiveEngine;
using UAM.Core.Models;

namespace UAM.AdaptiveEngine.Tests;

public sealed class RecommendationApproverTests
{
    private static OptimizationRecommendation CreateRecommendation(
        bool actionable)
    {
        var profile = new GameProfile
        {
            GameId = "valorant",
            DirectionalGains = Enumerable.Range(0, 8)
                .ToDictionary(x => x, _ => 1.0),
            SpeedGains = Enum.GetValues<SpeedBand>()
                .ToDictionary(x => x, _ => 1.0),
            UpdatedAtUtc = TestData.BaseTimestamp
        };

        return new OptimizationRecommendation
        {
            GameId = "valorant",
            ProposedProfile = profile,
            IsActionable = actionable,
            RequiresApproval = actionable,
            Reason = actionable ? "test" : "insufficient evidence",
            CreatedAtUtc = TestData.BaseTimestamp
        };
    }

    [Fact]
    public void Approve_ActionableRecommendation_ReturnsProfile()
    {
        var approver = new RecommendationApprover();
        var recommendation = CreateRecommendation(actionable: true);

        var approved = approver.Approve(recommendation);

        Assert.Equal(recommendation.ProposedProfile.GameId, approved.GameId);
        Assert.True(approved.UpdatedAtUtc > TestData.BaseTimestamp);
    }

    [Fact]
    public void Approve_NonActionableRecommendation_Throws()
    {
        var approver = new RecommendationApprover();
        var recommendation = CreateRecommendation(actionable: false);

        Assert.Throws<InvalidOperationException>(
            () => approver.Approve(recommendation));
    }

    [Fact]
    public void Approve_RequiresApproverIdentifier()
    {
        var approver = new RecommendationApprover();
        var recommendation = CreateRecommendation(actionable: true);

        Assert.Throws<ArgumentException>(
            () => approver.Approve(recommendation, approvedBy: " "));
    }
}
