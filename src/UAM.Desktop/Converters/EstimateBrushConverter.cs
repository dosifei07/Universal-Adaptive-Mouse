using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop;

/// <summary>
/// Maps an estimate's tone and bias magnitude to a display brush. This is a
/// presentation concern only; it never changes analysis values.
/// </summary>
public sealed class EstimateBrushConverter : IValueConverter
{
    private static readonly IBrush NoData = Brush("#EEF0F3");
    private static readonly IBrush Insufficient = Brush("#F4E7C3");
    private static readonly IBrush Centered = Brush("#D8EFDF");

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        HeatmapTone tone;
        double intensity;

        switch (value)
        {
            case HeatmapCellViewModel cell:
                tone = cell.Tone;
                intensity = cell.Intensity;
                break;
            case SpeedEstimateViewModel speed:
                tone = speed.Tone;
                intensity = speed.Intensity;
                break;
            default:
                return NoData;
        }

        return tone switch
        {
            HeatmapTone.NoData => NoData,
            HeatmapTone.Insufficient => Insufficient,
            HeatmapTone.Centered => Centered,
            HeatmapTone.Overshoot => Blend(0xC0, 0x39, 0x2B, intensity),
            HeatmapTone.Undershoot => Blend(0x25, 0x63, 0xAE, intensity),
            _ => NoData
        };
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IBrush Blend(byte red, byte green, byte blue, double intensity)
    {
        var strength = Math.Clamp(intensity, 0.08, 1.0);
        var alpha = (byte)Math.Round(45 + (strength * 150));
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    private static IBrush Brush(string hex)
    {
        return new SolidColorBrush(Color.Parse(hex));
    }
}
