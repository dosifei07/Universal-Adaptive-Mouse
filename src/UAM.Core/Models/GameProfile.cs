namespace UAM.Core.Models;

/// <summary>
/// Personalized gain settings for one game. A profile contains adjustment
/// parameters only; the underlying <see cref="PlayerModel"/> is shared.
/// </summary>
public sealed record GameProfile
{
    public required string GameId { get; init; }

    public double BaseSensitivity { get; init; } = 1.0;

    public double Dpi { get; init; }

    public double InGameSensitivity { get; init; }

    public required IReadOnlyDictionary<int, double> DirectionalGains { get; init; }

    public required IReadOnlyDictionary<SpeedBand, double> SpeedGains { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>
    /// Formats a stable profile identifier for export file names.
    /// </summary>
    public string ToFileIdentifier()
    {
        var safeGameId = new string(GameId
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return $"uam-profile-{safeGameId}-{UpdatedAtUtc:yyyyMMddTHHmmssZ}";
    }
}
