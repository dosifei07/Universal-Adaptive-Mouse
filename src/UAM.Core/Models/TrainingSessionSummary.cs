namespace UAM.Core.Models;

/// <summary>
/// Aggregate view of a stored training session.
/// </summary>
public sealed record TrainingSessionSummary
{
    public required string SessionId { get; init; }

    public required string GameId { get; init; }

    public required string ScenarioId { get; init; }

    public DateTime FirstObservationUtc { get; init; }

    public DateTime LastObservationUtc { get; init; }

    public int ObservationCount { get; init; }
}
