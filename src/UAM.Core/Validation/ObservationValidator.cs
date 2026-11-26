using UAM.Core.Models;

namespace UAM.Core.Validation;

/// <summary>
/// Configurable data-quality rules applied before an observation may take part
/// in analysis or be persisted. These defaults are initial engineering guards,
/// not physiological thresholds.
/// </summary>
public sealed record ObservationValidationOptions
{
    public double MaxAbsoluteDirectionDegrees { get; init; } = 360.0;

    public double MaxSpeedPixelsPerSecond { get; init; } = 100_000.0;

    public double MaxAbsoluteErrorRatio { get; init; } = 2.0;

    public double MaxAcquisitionTimeMs { get; init; } = 60_000.0;

    public DateTime MinimumTimestampUtc { get; init; } = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public TimeSpan MaxFutureSkew { get; init; } = TimeSpan.FromHours(24);

    public bool RequireNonEmptyIds { get; init; } = true;
}

public enum ObservationValidationCode
{
    MissingSessionId,
    MissingGameId,
    MissingScenarioId,
    NonFiniteDirection,
    DirectionOutOfRange,
    NonFiniteSpeed,
    NegativeSpeed,
    SpeedOutlier,
    NonFiniteSignedError,
    SignedErrorOutlier,
    NonFiniteAcquisitionTime,
    NegativeAcquisitionTime,
    AcquisitionTimeOutlier,
    TimestampTooOld,
    TimestampInFuture,
    UndefinedMovementKind
}

public sealed record ObservationValidationResult
{
    public required IReadOnlyList<ObservationValidationCode> Codes { get; init; }

    public bool IsValid => Codes.Count == 0;
}

public sealed record RejectedObservation(
    TrainingObservation Observation,
    ObservationValidationResult Validation);

/// <summary>
/// Validates observations before they influence a model or enter storage.
/// </summary>
public sealed class ObservationValidator
{
    private readonly ObservationValidationOptions _options;
    private readonly Func<DateTime> _utcNow;

    public ObservationValidator(
        ObservationValidationOptions? options = null,
        Func<DateTime>? utcNow = null)
    {
        _options = options ?? new ObservationValidationOptions();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public ObservationValidationResult Validate(TrainingObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var codes = new List<ObservationValidationCode>();

        if (_options.RequireNonEmptyIds)
        {
            if (string.IsNullOrWhiteSpace(observation.SessionId))
            {
                codes.Add(ObservationValidationCode.MissingSessionId);
            }

            if (string.IsNullOrWhiteSpace(observation.GameId))
            {
                codes.Add(ObservationValidationCode.MissingGameId);
            }

            if (string.IsNullOrWhiteSpace(observation.ScenarioId))
            {
                codes.Add(ObservationValidationCode.MissingScenarioId);
            }
        }

        if (!double.IsFinite(observation.DirectionDegrees))
        {
            codes.Add(ObservationValidationCode.NonFiniteDirection);
        }
        else if (Math.Abs(observation.DirectionDegrees) > _options.MaxAbsoluteDirectionDegrees)
        {
            codes.Add(ObservationValidationCode.DirectionOutOfRange);
        }

        if (!double.IsFinite(observation.SpeedPixelsPerSecond))
        {
            codes.Add(ObservationValidationCode.NonFiniteSpeed);
        }
        else if (observation.SpeedPixelsPerSecond < 0)
        {
            codes.Add(ObservationValidationCode.NegativeSpeed);
        }
        else if (observation.SpeedPixelsPerSecond > _options.MaxSpeedPixelsPerSecond)
        {
            codes.Add(ObservationValidationCode.SpeedOutlier);
        }

        if (!double.IsFinite(observation.SignedErrorRatio))
        {
            codes.Add(ObservationValidationCode.NonFiniteSignedError);
        }
        else if (Math.Abs(observation.SignedErrorRatio) > _options.MaxAbsoluteErrorRatio)
        {
            codes.Add(ObservationValidationCode.SignedErrorOutlier);
        }

        if (!double.IsFinite(observation.AcquisitionTimeMs))
        {
            codes.Add(ObservationValidationCode.NonFiniteAcquisitionTime);
        }
        else if (observation.AcquisitionTimeMs < 0)
        {
            codes.Add(ObservationValidationCode.NegativeAcquisitionTime);
        }
        else if (observation.AcquisitionTimeMs > _options.MaxAcquisitionTimeMs)
        {
            codes.Add(ObservationValidationCode.AcquisitionTimeOutlier);
        }

        var timestamp = observation.TimestampUtc;
        if (timestamp.Kind == DateTimeKind.Local)
        {
            timestamp = timestamp.ToUniversalTime();
        }

        if (timestamp < _options.MinimumTimestampUtc)
        {
            codes.Add(ObservationValidationCode.TimestampTooOld);
        }
        else if (timestamp > _utcNow() + _options.MaxFutureSkew)
        {
            codes.Add(ObservationValidationCode.TimestampInFuture);
        }

        if (!Enum.IsDefined(observation.MovementKind))
        {
            codes.Add(ObservationValidationCode.UndefinedMovementKind);
        }

        return new ObservationValidationResult { Codes = codes };
    }
}
