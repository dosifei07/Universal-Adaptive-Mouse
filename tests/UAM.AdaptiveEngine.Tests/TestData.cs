using UAM.Core.Models;
using UAM.Core.Validation;

namespace UAM.AdaptiveEngine.Tests;

internal static class TestData
{
    public static readonly DateTime BaseTimestamp =
        new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    public static DateTime Now => BaseTimestamp.AddHours(1);

    public static ObservationValidator CreateValidator(DateTime? now = null)
    {
        return new ObservationValidator(utcNow: () => now ?? Now);
    }

    public static TrainingObservation Observation(
        int sector = 0,
        double errorRatio = 0,
        double speed = 650,
        double directionOffset = 0,
        string gameId = "valorant",
        string sessionId = "session-001",
        string scenarioId = "scenario-001",
        DateTime? timestampUtc = null,
        MovementKind movementKind = MovementKind.Flick,
        bool hit = false)
    {
        return new TrainingObservation
        {
            SessionId = sessionId,
            GameId = gameId,
            ScenarioId = scenarioId,
            TimestampUtc = timestampUtc ?? BaseTimestamp,
            DirectionDegrees = (sector * 45.0) + directionOffset,
            SpeedPixelsPerSecond = speed,
            SignedErrorRatio = errorRatio,
            AcquisitionTimeMs = 400,
            Hit = hit,
            MovementKind = movementKind
        };
    }

    public static IReadOnlyList<TrainingObservation> Repeat(
        int count,
        int sector = 0,
        double errorRatio = 0,
        double speed = 650,
        string gameId = "valorant")
    {
        return Enumerable.Range(0, count)
            .Select(_ => Observation(
                sector: sector,
                errorRatio: errorRatio,
                speed: speed,
                gameId: gameId))
            .ToList();
    }

    public static PlayerModel Model(
        IEnumerable<DirectionalEstimate>? directional = null,
        IEnumerable<SpeedEstimate>? speed = null,
        string playerId = "player-001")
    {
        return new PlayerModel
        {
            PlayerId = playerId,
            UpdatedAtUtc = BaseTimestamp,
            DirectionalEstimates = directional?.ToList() ?? [],
            SpeedEstimates = speed?.ToList() ?? []
        };
    }

    public static DirectionalEstimate Direction(
        int sector,
        double error,
        int samples = 100,
        double? confidence = null)
    {
        return new DirectionalEstimate
        {
            Sector = sector,
            SampleCount = samples,
            MeanErrorRatio = error,
            Confidence = confidence ?? DefaultConfidence(samples)
        };
    }

    public static SpeedEstimate Speed(
        SpeedBand band,
        double error,
        int samples = 100,
        double? confidence = null)
    {
        return new SpeedEstimate
        {
            Band = band,
            SampleCount = samples,
            MeanErrorRatio = error,
            Confidence = confidence ?? DefaultConfidence(samples)
        };
    }

    private static double DefaultConfidence(int samples)
    {
        // Mirrors PerformanceAnalyzer: below ten samples confidence is zero.
        return samples < 10
            ? 0
            : Math.Min(0.95, samples / (double)(samples + 30));
    }
}
