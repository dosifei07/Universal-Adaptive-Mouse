using System.Globalization;
using UAM.Core.Models;

namespace UAM.Desktop.Shared.Formatting;

/// <summary>
/// Display formatting shared by the desktop pages and their tests. These
/// helpers format real values; they never invent or smooth data.
/// </summary>
public static class UamFormat
{
    public static string Count(int value)
    {
        return value.ToString("N0", CultureInfo.CurrentCulture);
    }

    public static string Percent(double value)
    {
        return value.ToString("P0", CultureInfo.CurrentCulture);
    }

    public static string Gain(double value)
    {
        return value.ToString("0.000", CultureInfo.CurrentCulture) + "x";
    }

    public static string Error(double value)
    {
        return value.ToString("+0.0000;-0.0000;0.0000", CultureInfo.CurrentCulture);
    }

    public static string Date(DateTime? value)
    {
        return value is null
            ? "No data yet"
            : value.Value.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.CurrentCulture);
    }

    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    public static string Direction(int sector)
    {
        return sector switch
        {
            0 => "Right",
            1 => "Upper right",
            2 => "Up",
            3 => "Upper left",
            4 => "Left",
            5 => "Lower left",
            6 => "Down",
            7 => "Lower right",
            _ => $"Sector {sector}"
        };
    }

    public static string DirectionArrow(int sector)
    {
        return sector switch
        {
            0 => "→",
            1 => "↗",
            2 => "↑",
            3 => "↖",
            4 => "←",
            5 => "↙",
            6 => "↓",
            7 => "↘",
            _ => "•"
        };
    }

    public static string Bias(double meanErrorRatio)
    {
        if (Math.Abs(meanErrorRatio) < 0.005)
        {
            return "Centered";
        }

        return meanErrorRatio > 0
            ? "Overshoot"
            : "Undershoot";
    }

    public static string Confidence(double confidence)
    {
        return confidence <= 0 ? "Insufficient" : Percent(confidence);
    }

    public static string Band(SpeedBand band)
    {
        return band switch
        {
            SpeedBand.Slow => "Slow",
            SpeedBand.Medium => "Medium",
            SpeedBand.Fast => "Fast",
            _ => band.ToString()
        };
    }

    public static string Status(RecommendationStatus status)
    {
        return status switch
        {
            RecommendationStatus.Pending => "Pending",
            RecommendationStatus.Approved => "Approved",
            RecommendationStatus.Rejected => "Rejected",
            RecommendationStatus.Superseded => "Superseded",
            RecommendationStatus.Informational => "Informational",
            _ => status.ToString()
        };
    }
}
