namespace UAM.Core.Models;

public enum MovementKind
{
    Flick,
    Tracking,
    MicroCorrection
}

public enum SpeedBand
{
    Slow,
    Medium,
    Fast
}

/// <summary>
/// The provider-neutral unit of training data. Every training provider must
/// eventually produce this shape, regardless of whether the source is a
/// benchmark, a game integration, or a synthetic dataset.
/// </summary>
public sealed record TrainingObservation
{
    public required string SessionId { get; init; }

    public required string GameId { get; init; }

    public required string ScenarioId { get; init; }

    public DateTime TimestampUtc { get; init; }

    /// <summary>
    /// Movement direction in degrees, measured counter-clockwise from screen
    /// right (0 = right, 90 = up, 180 = left, 270 = down).
    /// </summary>
    public double DirectionDegrees { get; init; }

    /// <summary>Mouse movement speed in pixels per second.</summary>
    public double SpeedPixelsPerSecond { get; init; }

    /// <summary>
    /// Signed endpoint error divided by target distance.
    /// Positive means overshoot, negative means undershoot.
    /// </summary>
    public double SignedErrorRatio { get; init; }

    public double AcquisitionTimeMs { get; init; }

    public bool Hit { get; init; }

    public MovementKind MovementKind { get; init; }
}
