using System.Globalization;
using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;

namespace UAM.Desktop.ViewModels;

/// <summary>Read-only display of one gain in a proposed or approved profile.</summary>
public sealed class GainRowViewModel
{
    public GainRowViewModel(string label, string? arrow, double gain)
    {
        Label = label;
        Arrow = arrow;
        Gain = gain;
        GainText = UamFormat.Gain(gain);
        DeltaText = DeltaFromNeutral(gain);
        IsNeutral = Math.Abs(gain - 1.0) < 0.0005;
    }

    public string Label { get; }

    public string? Arrow { get; }

    public double Gain { get; }

    public string GainText { get; }

    /// <summary>Change from the neutral 1.000x gain, e.g. "+4.0%".</summary>
    public string DeltaText { get; }

    public bool IsNeutral { get; }

    public static string DeltaFromNeutral(double gain)
    {
        return ((gain - 1.0) * 100.0).ToString(
            "+0.0;-0.0;0.0",
            CultureInfo.CurrentCulture) + "%";
    }

    public static IReadOnlyList<GainRowViewModel> DirectionalRows(
        IReadOnlyDictionary<int, double> gains)
    {
        return gains
            .OrderBy(x => x.Key)
            .Select(x => new GainRowViewModel(
                UamFormat.Direction(x.Key),
                UamFormat.DirectionArrow(x.Key),
                x.Value))
            .ToList();
    }

    public static IReadOnlyList<GainRowViewModel> SpeedRows(
        IReadOnlyDictionary<SpeedBand, double> gains)
    {
        return gains
            .OrderBy(x => (int)x.Key)
            .Select(x => new GainRowViewModel(
                UamFormat.Band(x.Key),
                null,
                x.Value))
            .ToList();
    }
}
