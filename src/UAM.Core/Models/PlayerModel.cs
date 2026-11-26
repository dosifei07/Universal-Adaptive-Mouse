namespace UAM.Core.Models;

/// <summary>
/// The player's underlying aiming characteristics, independent of any single
/// game. Game-specific profiles are derived from this model plus per-game data.
/// </summary>
public sealed record PlayerModel
{
    public required string PlayerId { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public required IReadOnlyList<DirectionalEstimate> DirectionalEstimates { get; init; }

    public required IReadOnlyList<SpeedEstimate> SpeedEstimates { get; init; }

    public static PlayerModel Empty(string playerId) => new()
    {
        PlayerId = playerId,
        UpdatedAtUtc = DateTime.UtcNow,
        DirectionalEstimates = [],
        SpeedEstimates = []
    };
}

public sealed record DirectionalEstimate
{
    /// <summary>Direction sector, 0 through 7 (0 = right, increasing counter-clockwise).</summary>
    public int Sector { get; init; }

    public int SampleCount { get; init; }

    public double MeanErrorRatio { get; init; }

    /// <summary>
    /// Statistical confidence in [0, 1). Zero when the sample count is below
    /// the analyzer's minimum.
    /// </summary>
    public double Confidence { get; init; }
}

public sealed record SpeedEstimate
{
    public SpeedBand Band { get; init; }

    public int SampleCount { get; init; }

    public double MeanErrorRatio { get; init; }

    public double Confidence { get; init; }
}
