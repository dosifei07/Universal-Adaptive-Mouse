using UAM.Core.Models;
using UAM.Core.Validation;

namespace UAM.Analytics;

/// <summary>
/// Tunable parameters for the v0.1 analyzer. The speed thresholds are
/// experimental defaults, not universal physiological boundaries.
/// </summary>
public sealed record AnalyticsOptions
{
    public int DirectionSectors { get; init; } = 8;

    public int MinimumSamples { get; init; } = 10;

    public double SlowSpeedUpperBound { get; init; } = 300.0;

    public double FastSpeedLowerBound { get; init; } = 900.0;

    /// <summary>Larger values make confidence grow more slowly with sample count.</summary>
    public double ConfidencePrior { get; init; } = 30.0;
}

public sealed record AnalysisReport
{
    public required PlayerModel Model { get; init; }

    public int AcceptedCount { get; init; }

    public int RejectedCount { get; init; }

    public required IReadOnlyList<RejectedObservation> Rejections { get; init; }
}

/// <summary>
/// Turns raw observations into a directional and speed performance model.
/// The analyzer is independent of any input source, game, or driver.
/// </summary>
public sealed class PerformanceAnalyzer
{
    private readonly AnalyticsOptions _options;
    private readonly ObservationValidator _validator;

    public PerformanceAnalyzer(
        AnalyticsOptions? options = null,
        ObservationValidator? validator = null)
    {
        _options = options ?? new AnalyticsOptions();
        _validator = validator ?? new ObservationValidator();

        if (_options.DirectionSectors <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DirectionSectors must be positive.");
        }

        if (_options.MinimumSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MinimumSamples must be positive.");
        }

        if (_options.ConfidencePrior <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ConfidencePrior must be positive.");
        }

        if (_options.SlowSpeedUpperBound <= 0 ||
            _options.FastSpeedLowerBound <= _options.SlowSpeedUpperBound)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Speed band boundaries must be ordered and positive.");
        }
    }

    public PlayerModel Analyze(
        string playerId,
        IEnumerable<TrainingObservation> observations)
    {
        return AnalyzeWithDiagnostics(playerId, observations).Model;
    }

    public AnalysisReport AnalyzeWithDiagnostics(
        string playerId,
        IEnumerable<TrainingObservation> observations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentNullException.ThrowIfNull(observations);

        var accepted = new List<TrainingObservation>();
        var rejections = new List<RejectedObservation>();

        foreach (var observation in observations)
        {
            var validation = _validator.Validate(observation);

            if (validation.IsValid)
            {
                accepted.Add(observation);
            }
            else
            {
                rejections.Add(new RejectedObservation(observation, validation));
            }
        }

        var directional = accepted
            .GroupBy(x => GetDirectionSector(x.DirectionDegrees))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var errors = group
                    .Select(x => x.SignedErrorRatio)
                    .ToList();

                return new DirectionalEstimate
                {
                    Sector = group.Key,
                    SampleCount = errors.Count,
                    MeanErrorRatio = errors.Average(),
                    Confidence = CalculateConfidence(errors.Count)
                };
            })
            .ToList();

        var speed = accepted
            .GroupBy(x => GetSpeedBand(x.SpeedPixelsPerSecond))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var errors = group
                    .Select(x => x.SignedErrorRatio)
                    .ToList();

                return new SpeedEstimate
                {
                    Band = group.Key,
                    SampleCount = errors.Count,
                    MeanErrorRatio = errors.Average(),
                    Confidence = CalculateConfidence(errors.Count)
                };
            })
            .ToList();

        var model = new PlayerModel
        {
            PlayerId = playerId,
            UpdatedAtUtc = DateTime.UtcNow,
            DirectionalEstimates = directional,
            SpeedEstimates = speed
        };

        return new AnalysisReport
        {
            Model = model,
            AcceptedCount = accepted.Count,
            RejectedCount = rejections.Count,
            Rejections = rejections
        };
    }

    public int GetDirectionSector(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        var sectorWidth = 360.0 / _options.DirectionSectors;

        return (int)Math.Floor(
            (normalized + (sectorWidth / 2.0)) / sectorWidth
        ) % _options.DirectionSectors;
    }

    public SpeedBand GetSpeedBand(double speed)
    {
        if (speed < _options.SlowSpeedUpperBound)
        {
            return SpeedBand.Slow;
        }

        return speed < _options.FastSpeedLowerBound
            ? SpeedBand.Medium
            : SpeedBand.Fast;
    }

    public double CalculateConfidence(int count)
    {
        if (count < _options.MinimumSamples)
        {
            return 0;
        }

        return count / (double)(count + _options.ConfidencePrior);
    }
}
