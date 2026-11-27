using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;

namespace UAM.Desktop.ViewModels;

/// <summary>One speed band row in the analysis tables.</summary>
public sealed class SpeedEstimateViewModel
{
    private SpeedEstimateViewModel(SpeedBand band, SpeedEstimate? estimate)
    {
        Band = band;
        BandText = UamFormat.Band(band);
        SampleCount = estimate?.SampleCount ?? 0;
        MeanErrorRatio = estimate?.MeanErrorRatio ?? 0;
        Confidence = estimate?.Confidence ?? 0;

        Tone = EstimateTone.Classify(SampleCount, Confidence, MeanErrorRatio);
        Intensity = EstimateTone.Intensity(Tone, MeanErrorRatio);

        SampleText = HasData ? UamFormat.Count(SampleCount) : "No data";
        ErrorText = HasData ? UamFormat.Error(MeanErrorRatio) : "—";
        ConfidenceText = HasData ? UamFormat.Confidence(Confidence) : "—";
        BiasText = Tone switch
        {
            HeatmapTone.NoData => "No data",
            HeatmapTone.Insufficient => "Insufficient data",
            _ => UamFormat.Bias(MeanErrorRatio)
        };
    }

    public SpeedBand Band { get; }

    public string BandText { get; }

    public int SampleCount { get; }

    public double MeanErrorRatio { get; }

    public double Confidence { get; }

    public bool HasData => SampleCount > 0;

    public HeatmapTone Tone { get; }

    public double Intensity { get; }

    public string SampleText { get; }

    public string ErrorText { get; }

    public string ConfidenceText { get; }

    public string BiasText { get; }

    /// <summary>
    /// Always returns one row per speed band, slow to fast, with explicit
    /// "no data" rows for bands the analyzer produced nothing for.
    /// </summary>
    public static IReadOnlyList<SpeedEstimateViewModel> BuildAll(
        IEnumerable<SpeedEstimate>? estimates)
    {
        var byBand = (estimates ?? [])
            .GroupBy(x => x.Band)
            .ToDictionary(x => x.Key, x => x.First());

        return Enum.GetValues<SpeedBand>()
            .OrderBy(x => (int)x)
            .Select(band => new SpeedEstimateViewModel(
                band,
                byBand.GetValueOrDefault(band)))
            .ToList();
    }
}
