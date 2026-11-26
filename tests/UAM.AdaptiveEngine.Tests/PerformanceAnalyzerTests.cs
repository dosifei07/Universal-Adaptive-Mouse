using UAM.Analytics;
using UAM.Core.Models;

namespace UAM.AdaptiveEngine.Tests;

public sealed class PerformanceAnalyzerTests
{
    private static PerformanceAnalyzer CreateAnalyzer(
        AnalyticsOptions? options = null)
    {
        return new PerformanceAnalyzer(options, TestData.CreateValidator());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(22.4, 0)]
    [InlineData(22.5, 1)]
    [InlineData(45, 1)]
    [InlineData(67.5, 2)]
    [InlineData(90, 2)]
    [InlineData(135, 3)]
    [InlineData(180, 4)]
    [InlineData(225, 5)]
    [InlineData(270, 6)]
    [InlineData(315, 7)]
    [InlineData(337.5, 0)]
    [InlineData(359.9, 0)]
    [InlineData(360, 0)]
    [InlineData(-45, 7)]
    public void GetDirectionSector_ReturnsExpectedSector(
        double degrees,
        int expected)
    {
        var analyzer = CreateAnalyzer();

        Assert.Equal(expected, analyzer.GetDirectionSector(degrees));
    }

    [Theory]
    [InlineData(0, SpeedBand.Slow)]
    [InlineData(299.99, SpeedBand.Slow)]
    [InlineData(300, SpeedBand.Medium)]
    [InlineData(899.99, SpeedBand.Medium)]
    [InlineData(900, SpeedBand.Fast)]
    public void GetSpeedBand_UsesConfiguredBoundaries(
        double speed,
        SpeedBand expected)
    {
        var analyzer = CreateAnalyzer();

        Assert.Equal(expected, analyzer.GetSpeedBand(speed));
    }

    [Fact]
    public void CalculateConfidence_IsZeroBelowMinimumSamples()
    {
        var analyzer = CreateAnalyzer();

        Assert.Equal(0, analyzer.CalculateConfidence(0));
        Assert.Equal(0, analyzer.CalculateConfidence(9));
        Assert.Equal(0.25, analyzer.CalculateConfidence(10), 10);
        Assert.Equal(0.5, analyzer.CalculateConfidence(30), 10);
    }

    [Fact]
    public void Analyze_GroupsByDirectionAndSpeed()
    {
        var analyzer = CreateAnalyzer();

        var observations = new[]
        {
            TestData.Observation(sector: 0, errorRatio: 0.10, speed: 200),
            TestData.Observation(sector: 0, errorRatio: 0.20, speed: 600),
            TestData.Observation(sector: 0, errorRatio: -0.10, speed: 1000),
            TestData.Observation(sector: 4, errorRatio: 0.05, speed: 200)
        };

        var model = analyzer.Analyze("player-001", observations);

        var sectorZero = Assert.Single(
            model.DirectionalEstimates,
            x => x.Sector == 0);

        Assert.Equal(3, sectorZero.SampleCount);
        Assert.Equal(0.20 / 3.0, sectorZero.MeanErrorRatio, 10);

        var sectorFour = Assert.Single(
            model.DirectionalEstimates,
            x => x.Sector == 4);

        Assert.Equal(1, sectorFour.SampleCount);
        Assert.Equal(0.05, sectorFour.MeanErrorRatio, 10);

        Assert.Equal(3, model.SpeedEstimates.Count);
        Assert.Equal(
            2,
            model.SpeedEstimates.Single(x => x.Band == SpeedBand.Slow).SampleCount);
        Assert.Equal(
            1,
            model.SpeedEstimates.Single(x => x.Band == SpeedBand.Medium).SampleCount);
        Assert.Equal(
            1,
            model.SpeedEstimates.Single(x => x.Band == SpeedBand.Fast).SampleCount);
    }

    [Fact]
    public void Analyze_RejectsInvalidObservations()
    {
        var analyzer = CreateAnalyzer();

        var valid = TestData.Repeat(12, sector: 0, errorRatio: 0.2);
        var invalid = Enumerable.Range(0, 5)
            .Select(_ => TestData.Observation(
                sector: 0,
                errorRatio: double.NaN))
            .ToList();

        var report = analyzer.AnalyzeWithDiagnostics(
            "player-001",
            valid.Concat(invalid));

        Assert.Equal(12, report.AcceptedCount);
        Assert.Equal(5, report.RejectedCount);
        Assert.Equal(5, report.Rejections.Count);
        Assert.All(
            report.Rejections,
            rejection => Assert.True(
                double.IsNaN(rejection.Observation.SignedErrorRatio)));

        var sectorZero = Assert.Single(report.Model.DirectionalEstimates);
        Assert.Equal(12, sectorZero.SampleCount);
        Assert.Equal(0.2, sectorZero.MeanErrorRatio, 10);
    }

    [Fact]
    public void Analyze_EmptyInputProducesEmptyModel()
    {
        var analyzer = CreateAnalyzer();

        var report = analyzer.AnalyzeWithDiagnostics(
            "player-001",
            Array.Empty<TrainingObservation>());

        Assert.Empty(report.Model.DirectionalEstimates);
        Assert.Empty(report.Model.SpeedEstimates);
        Assert.Equal(0, report.AcceptedCount);
        Assert.Equal(0, report.RejectedCount);
    }

    [Fact]
    public void Analyze_UsesAllSectorsIncludingWrapAround()
    {
        var analyzer = CreateAnalyzer();

        var observations = new[]
        {
            TestData.Observation(directionOffset: -90),
            TestData.Observation(directionOffset: 350)
        };

        var model = analyzer.Analyze("player-001", observations);

        Assert.Contains(model.DirectionalEstimates, x => x.Sector == 6);
        Assert.Contains(model.DirectionalEstimates, x => x.Sector == 0);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAnalyzer(new AnalyticsOptions { DirectionSectors = 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAnalyzer(new AnalyticsOptions { MinimumSamples = 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAnalyzer(new AnalyticsOptions { ConfidencePrior = 0 }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAnalyzer(new AnalyticsOptions
            {
                SlowSpeedUpperBound = 500,
                FastSpeedLowerBound = 400
            }));
    }
}
