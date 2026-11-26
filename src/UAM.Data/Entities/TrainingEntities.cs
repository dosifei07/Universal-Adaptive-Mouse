using Microsoft.EntityFrameworkCore;
using UAM.Core.Models;

namespace UAM.Data.Entities;

/// <summary>
/// Persistence-only shape. Domain models never depend on EF Core.
/// </summary>
public sealed class TrainingSessionEntity
{
    public string SessionId { get; set; } = "";

    public string GameId { get; set; } = "";

    public string ScenarioId { get; set; } = "";

    public DateTime FirstObservationUtc { get; set; }

    public DateTime LastObservationUtc { get; set; }

    public int ObservationCount { get; set; }

    public List<TrainingObservationEntity> Observations { get; set; } = [];
}

public sealed class TrainingObservationEntity
{
    public long Id { get; set; }

    public string SessionId { get; set; } = "";

    public string GameId { get; set; } = "";

    public string ScenarioId { get; set; } = "";

    public DateTime TimestampUtc { get; set; }

    public double DirectionDegrees { get; set; }

    public double SpeedPixelsPerSecond { get; set; }

    public double SignedErrorRatio { get; set; }

    public double AcquisitionTimeMs { get; set; }

    public bool Hit { get; set; }

    public MovementKind MovementKind { get; set; }

    public TrainingSessionEntity? Session { get; set; }
}
