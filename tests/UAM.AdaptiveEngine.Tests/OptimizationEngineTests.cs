using UAM.AdaptiveEngine;
using UAM.Core.Models;

namespace UAM.AdaptiveEngine.Tests;

public sealed class OptimizationEngineTests
{
    private static readonly OptimizationEngine Engine = new();

    private static OptimizationRecommendation Recommend(
        PlayerModel global,
        PlayerModel? game = null)
    {
        return Engine.BuildRecommendation(
            "valorant",
            global,
            game ?? TestData.Model());
    }

    [Fact]
    public void NeutralObservations_KeepGainsAtOne()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0, samples: 200)],
            speed: [TestData.Speed(SpeedBand.Medium, 0, samples: 200)]);

        var recommendation = Recommend(global, global);

        Assert.All(
            recommendation.ProposedProfile.DirectionalGains.Values,
            gain => Assert.Equal(1.0, gain, 12));

        Assert.All(
            recommendation.ProposedProfile.SpeedGains.Values,
            gain => Assert.Equal(1.0, gain, 12));

        Assert.False(recommendation.IsActionable);
        Assert.False(recommendation.RequiresApproval);
    }

    [Fact]
    public void ConsistentOvershoot_ReducesGain()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.5, samples: 200)],
            speed: [TestData.Speed(SpeedBand.Medium, 0.5, samples: 200)]);

        var recommendation = Recommend(global, global);

        var gain = recommendation.ProposedProfile.DirectionalGains[0];

        Assert.True(gain < 1.0);
        Assert.InRange(gain, 0.98, 1.0);
        Assert.True(recommendation.IsActionable);
        Assert.True(recommendation.RequiresApproval);
    }

    [Fact]
    public void ConsistentUndershoot_IncreasesGain()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(3, -0.5, samples: 200)],
            speed: [TestData.Speed(SpeedBand.Fast, -0.5, samples: 200)]);

        var recommendation = Recommend(global, global);

        var gain = recommendation.ProposedProfile.DirectionalGains[3];

        Assert.True(gain > 1.0);
        Assert.InRange(gain, 1.0, 1.02);
    }

    [Fact]
    public void MissingDirection_RemainsNeutral()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.5, samples: 200)]);

        var recommendation = Recommend(global, global);

        Assert.Equal(1.0, recommendation.ProposedProfile.DirectionalGains[3], 12);
        Assert.True(recommendation.IsActionable);
    }

    [Fact]
    public void InsufficientSamples_ProduceNoActionableRecommendation()
    {
        // Confidence is zero below the analyzer's minimum of ten samples.
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.9, samples: 9)],
            speed: [TestData.Speed(SpeedBand.Slow, 0.9, samples: 9)]);

        var recommendation = Recommend(global, global);

        Assert.All(
            recommendation.ProposedProfile.DirectionalGains.Values,
            gain => Assert.Equal(1.0, gain, 12));

        Assert.All(
            recommendation.ProposedProfile.SpeedGains.Values,
            gain => Assert.Equal(1.0, gain, 12));

        Assert.False(recommendation.IsActionable);
        Assert.False(recommendation.RequiresApproval);
        Assert.Contains("Insufficient evidence", recommendation.Reason);
    }

    [Fact]
    public void LowConfidence_DoesNotProduceAdjustment()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 1.0, samples: 100, confidence: 0.19)],
            speed: [TestData.Speed(SpeedBand.Slow, 1.0, samples: 100, confidence: 0.19)]);

        var recommendation = Recommend(global, global);

        Assert.All(
            recommendation.ProposedProfile.DirectionalGains.Values,
            gain => Assert.Equal(1.0, gain, 12));

        Assert.False(recommendation.IsActionable);
    }

    [Fact]
    public void ConfidenceAtThreshold_ProducesAdjustment()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.5, samples: 100, confidence: 0.20)]);

        var recommendation = Recommend(global, global);

        Assert.True(
            recommendation.ProposedProfile.DirectionalGains[0] < 1.0);
        Assert.True(recommendation.IsActionable);
    }

    [Fact]
    public void LargeErrors_StayWithinSafetyLimits()
    {
        var directional = Enumerable.Range(0, 8)
            .Select(sector => TestData.Direction(
                sector,
                1.9,
                samples: 500,
                confidence: 1.0))
            .ToList();

        var speed = Enum.GetValues<SpeedBand>()
            .Select(band => TestData.Speed(
                band,
                1.9,
                samples: 500,
                confidence: 1.0))
            .ToList();

        var model = TestData.Model(directional, speed);
        var recommendation = Recommend(model, model);

        Assert.All(
            recommendation.ProposedProfile.DirectionalGains.Values,
            gain => Assert.InRange(gain, 0.98, 1.02));
        Assert.All(
            recommendation.ProposedProfile.SpeedGains.Values,
            gain => Assert.InRange(gain, 0.98, 1.02));

        Assert.Equal(
            0.98,
            recommendation.ProposedProfile.DirectionalGains[0],
            10);
    }

    [Fact]
    public void GameSpecificData_InfluencesProfile()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.0, samples: 200)],
            speed: [TestData.Speed(SpeedBand.Medium, 0.0, samples: 200)]);

        var game = TestData.Model(
            directional: [TestData.Direction(0, 0.8, samples: 200)],
            speed: [TestData.Speed(SpeedBand.Medium, 0.8, samples: 200)]);

        var withGameData = Recommend(global, game);
        var globalOnly = Recommend(global);

        Assert.True(
            withGameData.ProposedProfile.DirectionalGains[0] <
            globalOnly.ProposedProfile.DirectionalGains[0]);

        Assert.True(
            withGameData.ProposedProfile.SpeedGains[SpeedBand.Medium] <
            globalOnly.ProposedProfile.SpeedGains[SpeedBand.Medium]);
    }

    [Fact]
    public void NoGameData_UsesGlobalModelAsPrior()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.8, samples: 200)],
            speed: [TestData.Speed(SpeedBand.Fast, 0.8, samples: 200)]);

        var recommendation = Recommend(global, game: null);

        Assert.True(
            recommendation.ProposedProfile.DirectionalGains[0] < 1.0);
        Assert.True(
            recommendation.ProposedProfile.SpeedGains[SpeedBand.Fast] < 1.0);
        Assert.True(recommendation.IsActionable);
    }

    [Fact]
    public void MoreGameSamples_IncreaseGameInfluence()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.0, samples: 200, confidence: 0.5)],
            speed: []);

        // Fix confidence so this test isolates the sample-count blend weight.
        var smallGame = TestData.Model(
            directional: [TestData.Direction(0, 0.8, samples: 10, confidence: 0.5)]);
        var largeGame = TestData.Model(
            directional: [TestData.Direction(0, 0.8, samples: 300, confidence: 0.5)]);

        var small = Recommend(global, smallGame);
        var large = Recommend(global, largeGame);

        Assert.True(
            large.ProposedProfile.DirectionalGains[0] <
            small.ProposedProfile.DirectionalGains[0]);
    }

    [Fact]
    public void Recommendation_ContainsCompleteGainTables()
    {
        var recommendation = Recommend(TestData.Model());

        Assert.Equal(8, recommendation.ProposedProfile.DirectionalGains.Count);
        Assert.Equal(
            Enum.GetValues<SpeedBand>().Length,
            recommendation.ProposedProfile.SpeedGains.Count);

        Assert.Equal("valorant", recommendation.GameId);
        Assert.Equal("valorant", recommendation.ProposedProfile.GameId);
        Assert.Equal(1.0, recommendation.ProposedProfile.BaseSensitivity);
        Assert.True(
            recommendation.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void HigherConfidence_ProducesLargerCorrection()
    {
        var low = TestData.Model(
            directional: [TestData.Direction(0, 0.5, confidence: 0.21)]);
        var high = TestData.Model(
            directional: [TestData.Direction(0, 0.5, confidence: 0.95)]);

        var lowRecommendation = Recommend(low);
        var highRecommendation = Recommend(high);

        Assert.True(
            highRecommendation.ProposedProfile.DirectionalGains[0] <
            lowRecommendation.ProposedProfile.DirectionalGains[0]);
    }

    [Fact]
    public void TinyBias_IsTreatedAsNoise()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.002, confidence: 0.5)],
            speed: [TestData.Speed(SpeedBand.Medium, 0.002, confidence: 0.5)]);

        var recommendation = Recommend(global, global);

        Assert.Equal(1.0, recommendation.ProposedProfile.DirectionalGains[0], 12);
        Assert.Equal(
            1.0,
            recommendation.ProposedProfile.SpeedGains[SpeedBand.Medium],
            12);
        Assert.False(recommendation.IsActionable);
    }

    [Fact]
    public void BiasAboveNoiseFloor_IsActionable()
    {
        var global = TestData.Model(
            directional: [TestData.Direction(0, 0.02, confidence: 0.5)]);

        var recommendation = Recommend(global, global);

        Assert.True(
            recommendation.ProposedProfile.DirectionalGains[0] < 1.0);
        Assert.True(recommendation.IsActionable);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptimizationEngine(new OptimizationEngineOptions
            {
                MaximumAdjustment = 0
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptimizationEngine(new OptimizationEngineOptions
            {
                CorrectionStrength = -1
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptimizationEngine(new OptimizationEngineOptions
            {
                MinimumConfidence = 1.5
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OptimizationEngine(new OptimizationEngineOptions
            {
                MinimumActionableAdjustment = 0.05
            }));
    }
}
