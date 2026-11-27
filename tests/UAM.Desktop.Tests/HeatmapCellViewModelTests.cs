using UAM.Core.Models;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class HeatmapCellViewModelTests
{
    [Fact]
    public void BuildRing_AlwaysReturnsEightCellsInSectorOrder()
    {
        var ring = HeatmapCellViewModel.BuildRing(
            [Samples.Direction(4, -0.05), Samples.Direction(0, 0.10)]);

        Assert.Equal(8, ring.Count);

        for (var sector = 0; sector < 8; sector++)
        {
            Assert.Equal(sector, ring[sector].Sector);
        }
    }

    [Fact]
    public void BuildRing_MarksMissingSectorsAsNoDataInsteadOfInventingValues()
    {
        var ring = HeatmapCellViewModel.BuildRing([Samples.Direction(0, 0.10)]);

        Assert.True(ring[0].HasData);

        foreach (var cell in ring.Skip(1))
        {
            Assert.False(cell.HasData);
            Assert.Equal(HeatmapTone.NoData, cell.Tone);
            Assert.Equal(0, cell.SampleCount);
            Assert.Equal(0, cell.Intensity);
        }
    }

    [Fact]
    public void BuildRing_NullInputGivesEightNoDataCells()
    {
        var ring = HeatmapCellViewModel.BuildRing(null);

        Assert.Equal(8, ring.Count);
        Assert.All(ring, cell => Assert.Equal(HeatmapTone.NoData, cell.Tone));
    }

    [Fact]
    public void BuildRing_IgnoresSectorsOutsideZeroToSeven()
    {
        var ring = HeatmapCellViewModel.BuildRing(
            [Samples.Direction(8, 0.2), Samples.Direction(-1, 0.2)]);

        Assert.Equal(8, ring.Count);
        Assert.All(ring, cell => Assert.False(cell.HasData));
    }

    [Fact]
    public void ConfidentPositiveError_IsOvershootWithProportionalIntensity()
    {
        var cell = HeatmapCellViewModel.FromEstimate(Samples.Direction(0, 0.10));

        Assert.Equal(HeatmapTone.Overshoot, cell.Tone);
        Assert.Equal(0.10 / EstimateTone.FullScaleErrorRatio, cell.Intensity, 10);
    }

    [Fact]
    public void ConfidentNegativeError_IsUndershoot()
    {
        var cell = HeatmapCellViewModel.FromEstimate(Samples.Direction(4, -0.10));

        Assert.Equal(HeatmapTone.Undershoot, cell.Tone);
        Assert.True(cell.Intensity > 0);
    }

    [Fact]
    public void TinyError_IsCentered()
    {
        var cell = HeatmapCellViewModel.FromEstimate(Samples.Direction(2, 0.001));

        Assert.Equal(HeatmapTone.Centered, cell.Tone);
    }

    [Fact]
    public void ZeroConfidenceWithSamples_IsInsufficientAndNotDrawnStrongly()
    {
        var cell = HeatmapCellViewModel.FromEstimate(
            Samples.Direction(1, 0.40, samples: 4, confidence: 0));

        Assert.True(cell.HasData);
        Assert.Equal(HeatmapTone.Insufficient, cell.Tone);
        Assert.Equal(0, cell.Intensity);
    }

    [Fact]
    public void Intensity_IsClampedToOne()
    {
        var cell = HeatmapCellViewModel.FromEstimate(Samples.Direction(0, 1.5));

        Assert.Equal(1, cell.Intensity);
    }

    [Fact]
    public void Cell_CarriesRealEstimateValues()
    {
        var cell = HeatmapCellViewModel.FromEstimate(
            Samples.Direction(3, -0.07, samples: 321, confidence: 0.55));

        Assert.Equal(3, cell.Sector);
        Assert.Equal(321, cell.SampleCount);
        Assert.Equal(-0.07, cell.MeanErrorRatio);
        Assert.Equal(0.55, cell.Confidence);
        Assert.Equal("Upper left", cell.Direction);
        Assert.Equal("↖", cell.Arrow);
    }

    [Fact]
    public void SpeedRows_AlwaysCoverEveryBandSlowToFast()
    {
        var rows = SpeedEstimateViewModel.BuildAll([Samples.Speed(SpeedBand.Fast, 0.08)]);

        Assert.Equal(3, rows.Count);
        Assert.Equal(SpeedBand.Slow, rows[0].Band);
        Assert.Equal(SpeedBand.Medium, rows[1].Band);
        Assert.Equal(SpeedBand.Fast, rows[2].Band);
        Assert.False(rows[0].HasData);
        Assert.True(rows[2].HasData);
        Assert.Equal(HeatmapTone.Overshoot, rows[2].Tone);
    }
}
