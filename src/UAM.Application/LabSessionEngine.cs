using UAM.Core.Models;

namespace UAM.Application;

public readonly record struct LabPoint(double X, double Y);

public sealed record LabTarget
{
    public required int Index { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Radius { get; init; }
}

public sealed record LabTrial
{
    public required LabTarget Target { get; init; }

    public required LabPoint Start { get; init; }

    public required LabPoint End { get; init; }

    public required double StartMilliseconds { get; init; }

    public required double EndMilliseconds { get; init; }

    public required bool Hit { get; init; }
}

/// <summary>
/// A completed first-party pointer session. Coordinates are logical 2D canvas
/// units, not game camera angles; see <see cref="LabSessionEngine.Limitation"/>.
/// </summary>
public sealed record LabSessionCapture
{
    public required string SessionId { get; init; }

    public string GameId { get; init; } = "uam-lab";

    public string ScenarioId { get; init; } = "2d-flick-grid";

    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    public required IReadOnlyList<LabTrial> Trials { get; init; }
}

public sealed record LabSessionResult
{
    public required TrainingDocument Document { get; init; }

    public required ImportResult Import { get; init; }

    public int TrialCount { get; init; }

    public int HitCount { get; init; }

    public double HitRate => TrialCount == 0
        ? 0
        : (double)HitCount / TrialCount;
}

/// <summary>
/// Converts a first-party 2D pointer session into canonical observations.
/// This engine has no UI dependency; the Avalonia canvas only feeds it points.
/// </summary>
public sealed class LabSessionEngine
{
    public const string ProviderId = ProviderRegistry.UamLab;

    public const string CaptureMethod = "2d-canvas";

    /// <summary>
    /// Recorded limitation: this task measures 2D pointer flicks on a flat
    /// canvas. It is not first-person camera aiming and cannot model 3D
    /// sensitivity, field of view, or game-engine acceleration.
    /// </summary>
    public const string Limitation =
        "UAM Lab is a 2D pointer task, not first-person camera aiming. " +
        "Results describe mouse movement on a flat canvas and must not be " +
        "treated as game sensitivity measurements.";

    public TrainingDocument BuildDocument(LabSessionCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (string.IsNullOrWhiteSpace(capture.SessionId))
        {
            throw new ArgumentException(
                "A lab session needs a session id.",
                nameof(capture));
        }

        if (capture.Trials is null || capture.Trials.Count == 0)
        {
            throw new ArgumentException(
                "A lab session needs at least one completed trial.",
                nameof(capture));
        }

        if (capture.StartedUtc.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException(
                "The lab session start time must include a UTC kind.",
                nameof(capture));
        }

        var observations = capture.Trials
            .Select(trial => ToObservation(capture, trial))
            .ToList();

        return new TrainingDocument
        {
            SchemaVersion = TrainingDocumentFormat.CurrentSchemaVersion,
            Provider = ProviderId,
            CaptureMethod = CaptureMethod,
            SourceSessionId = capture.SessionId,
            Observations = observations
        };
    }

    private static TrainingObservation ToObservation(
        LabSessionCapture capture,
        LabTrial trial)
    {
        ArgumentNullException.ThrowIfNull(trial);

        var start = trial.Start;
        var target = trial.Target;
        var end = trial.End;

        var targetDx = target.X - start.X;
        var targetDy = target.Y - start.Y;
        var targetDistance = Math.Sqrt(targetDx * targetDx + targetDy * targetDy);

        var direction = targetDistance <= double.Epsilon
            ? 0
            : NormalizeDegrees(
                Math.Atan2(-targetDy, targetDx) * 180.0 / Math.PI);

        var moveDx = end.X - start.X;
        var moveDy = end.Y - start.Y;
        var moveDistance = Math.Sqrt(moveDx * moveDx + moveDy * moveDy);

        var durationMs = Math.Max(0, trial.EndMilliseconds - trial.StartMilliseconds);
        var speed = durationMs <= double.Epsilon
            ? 0
            : moveDistance / (durationMs / 1000.0);

        double errorRatio;

        if (targetDistance <= double.Epsilon)
        {
            errorRatio = 0;
        }
        else
        {
            var unitX = targetDx / targetDistance;
            var unitY = targetDy / targetDistance;
            var projected = moveDx * unitX + moveDy * unitY;

            // Positive means the endpoint travelled past the target centre
            // along the intended direction (overshoot).
            errorRatio = Math.Clamp(
                (projected - targetDistance) / targetDistance,
                -2.0,
                2.0);
        }

        return new TrainingObservation
        {
            SessionId = capture.SessionId,
            GameId = capture.GameId,
            ScenarioId = capture.ScenarioId,
            TimestampUtc = capture.StartedUtc.AddMilliseconds(trial.EndMilliseconds),
            DirectionDegrees = direction,
            SpeedPixelsPerSecond = Math.Clamp(speed, 0, 100_000),
            SignedErrorRatio = errorRatio,
            AcquisitionTimeMs = Math.Min(durationMs, 60_000),
            Hit = trial.Hit,
            MovementKind = Classify(trial, targetDistance, durationMs)
        };
    }

    private static MovementKind Classify(
        LabTrial trial,
        double targetDistance,
        double durationMs)
    {
        if (targetDistance > 0 && targetDistance < 60)
        {
            return MovementKind.MicroCorrection;
        }

        if (durationMs >= 600)
        {
            return MovementKind.Tracking;
        }

        return MovementKind.Flick;
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360.0;

        if (normalized < 0)
        {
            normalized += 360.0;
        }

        return normalized;
    }
}
