using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;

namespace UAM.Desktop.ViewModels;

/// <summary>How a view should classify one estimate.</summary>
public enum HeatmapTone
{
    /// <summary>No observations exist for this cell.</summary>
    NoData,

    /// <summary>Observations exist but confidence is below the analyzer minimum.</summary>
    Insufficient,

    /// <summary>Confident estimate with negligible bias.</summary>
    Centered,

    /// <summary>Confident estimate; the player tends to overshoot.</summary>
    Overshoot,

    /// <summary>Confident estimate; the player tends to undershoot.</summary>
    Undershoot
}

/// <summary>
/// Shared classification for directional and speed estimates so the heatmap
/// and the speed table agree on what "centered" and "insufficient" mean.
/// </summary>
public static class EstimateTone
{
    /// <summary>
    /// Mean error ratio that maps to full color intensity. This is a display
    /// scale only; it never feeds back into the analysis or optimization.
    /// </summary>
    public const double FullScaleErrorRatio = 0.25;

    private const double CenteredThreshold = 0.005;

    public static HeatmapTone Classify(
        int sampleCount,
        double confidence,
        double meanErrorRatio)
    {
        if (sampleCount <= 0)
        {
            return HeatmapTone.NoData;
        }

        if (confidence <= 0 || double.IsNaN(confidence))
        {
            return HeatmapTone.Insufficient;
        }

        if (Math.Abs(meanErrorRatio) < CenteredThreshold)
        {
            return HeatmapTone.Centered;
        }

        return meanErrorRatio > 0 ? HeatmapTone.Overshoot : HeatmapTone.Undershoot;
    }

    /// <summary>
    /// Magnitude of the bias in [0, 1] for colour scaling. Estimates without
    /// enough confidence report zero so weak evidence is never drawn strongly.
    /// </summary>
    public static double Intensity(HeatmapTone tone, double meanErrorRatio)
    {
        if (tone is HeatmapTone.NoData or HeatmapTone.Insufficient ||
            double.IsNaN(meanErrorRatio))
        {
            return 0;
        }

        return Math.Clamp(
            Math.Abs(meanErrorRatio) / FullScaleErrorRatio,
            0,
            1);
    }
}

/// <summary>
/// One of the eight directional cells in the aim heatmap. Immutable: a reload
/// builds a fresh ring rather than mutating cells, so views never show a
/// half-updated mix of old and new estimates.
/// </summary>
public sealed class HeatmapCellViewModel : ObservableObject
{
    public const int SectorCount = 8;

    private HeatmapCellViewModel(int sector, DirectionalEstimate? estimate)
    {
        Sector = sector;
        Direction = UamFormat.Direction(sector);
        Arrow = UamFormat.DirectionArrow(sector);

        SampleCount = estimate?.SampleCount ?? 0;
        MeanErrorRatio = estimate?.MeanErrorRatio ?? 0;
        Confidence = estimate?.Confidence ?? 0;

        Tone = EstimateTone.Classify(SampleCount, Confidence, MeanErrorRatio);
        Intensity = EstimateTone.Intensity(Tone, MeanErrorRatio);

        SampleText = HasData
            ? UamFormat.Count(SampleCount) + (SampleCount == 1 ? " sample" : " samples")
            : "No data";

        ErrorText = HasData ? UamFormat.Error(MeanErrorRatio) : "—";
        ConfidenceText = HasData ? UamFormat.Confidence(Confidence) : "—";

        BiasText = Tone switch
        {
            HeatmapTone.NoData => "No data",
            HeatmapTone.Insufficient => "Insufficient data",
            _ => UamFormat.Bias(MeanErrorRatio)
        };

        ToolTip = HasData
            ? $"{Direction}: {BiasText}, mean error {ErrorText}, " +
              $"confidence {ConfidenceText}, {SampleText}"
            : $"{Direction}: no observations yet";
    }

    public int Sector { get; }

    public string Direction { get; }

    public string Arrow { get; }

    public int SampleCount { get; }

    public double MeanErrorRatio { get; }

    public double Confidence { get; }

    public bool HasData => SampleCount > 0;

    public HeatmapTone Tone { get; }

    /// <summary>Bias magnitude in [0, 1]; see <see cref="EstimateTone.Intensity"/>.</summary>
    public double Intensity { get; }

    public string SampleText { get; }

    public string ErrorText { get; }

    public string ConfidenceText { get; }

    public string BiasText { get; }

    public string ToolTip { get; }

    public static HeatmapCellViewModel FromEstimate(DirectionalEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return new HeatmapCellViewModel(estimate.Sector, estimate);
    }

    public static HeatmapCellViewModel NoData(int sector)
    {
        return new HeatmapCellViewModel(sector, null);
    }

    /// <summary>
    /// Builds all eight cells in sector order. Sectors missing from the input
    /// become explicit "no data" cells; estimates outside 0–7 are ignored and
    /// never reshuffled into another cell.
    /// </summary>
    public static IReadOnlyList<HeatmapCellViewModel> BuildRing(
        IEnumerable<DirectionalEstimate>? estimates)
    {
        var bySector = (estimates ?? [])
            .Where(x => x.Sector is >= 0 and < SectorCount)
            .GroupBy(x => x.Sector)
            .ToDictionary(x => x.Key, x => x.First());

        return Enumerable.Range(0, SectorCount)
            .Select(sector => bySector.TryGetValue(sector, out var estimate)
                ? FromEstimate(estimate)
                : NoData(sector))
            .ToList();
    }
}
