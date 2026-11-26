using UAM.Core.Models;

namespace UAM.AdaptiveEngine;

/// <summary>
/// Safety and behavior limits for the optimization engine. The defaults are
/// intentionally conservative; the engine never proposes a large jump from a
/// single analysis pass.
/// </summary>
public sealed record OptimizationEngineOptions
{
    public double MaximumAdjustment { get; init; } = 0.02;

    public double CorrectionStrength { get; init; } = 0.15;

    /// <summary>
    /// Corrections smaller than this are treated as noise and left at neutral.
    /// This keeps the approval workflow focused on meaningful changes.
    /// </summary>
    public double MinimumActionableAdjustment { get; init; } = 0.001;

    /// <summary>
    /// Pseudo-count that controls how quickly game-specific data overrides the
    /// global player model.
    /// </summary>
    public double PriorWeight { get; init; } = 30.0;

    /// <summary>
    /// Below this confidence an estimate cannot produce an adjustment.
    /// </summary>
    public double MinimumConfidence { get; init; } = 0.20;

    public int DirectionSectors { get; init; } = 8;

    public double BaseSensitivity { get; init; } = 1.0;
}

/// <summary>
/// Translates performance estimates into conservative, game-specific gain
/// recommendations. This type has no knowledge of mice, drivers, games, or
/// operating systems, which keeps the mathematics independently testable.
/// </summary>
public sealed class OptimizationEngine
{
    private const double GainEpsilon = 1e-12;

    private readonly OptimizationEngineOptions _options;

    public OptimizationEngine(OptimizationEngineOptions? options = null)
    {
        _options = options ?? new OptimizationEngineOptions();

        if (_options.MaximumAdjustment <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaximumAdjustment must be positive.");
        }

        if (_options.CorrectionStrength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CorrectionStrength cannot be negative.");
        }

        if (_options.MinimumActionableAdjustment < 0 ||
            _options.MinimumActionableAdjustment >= _options.MaximumAdjustment)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MinimumActionableAdjustment must be non-negative and below MaximumAdjustment.");
        }

        if (_options.PriorWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PriorWeight must be positive.");
        }

        if (_options.MinimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MinimumConfidence must be in [0, 1].");
        }

        if (_options.DirectionSectors <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DirectionSectors must be positive.");
        }
    }

    public OptimizationRecommendation BuildRecommendation(
        string gameId,
        PlayerModel globalModel,
        PlayerModel gameModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(globalModel);
        ArgumentNullException.ThrowIfNull(gameModel);

        var directionalGains = new Dictionary<int, double>();
        var speedGains = new Dictionary<SpeedBand, double>();

        var highestConfidence = 0.0;
        var adjustedEstimateCount = 0;

        for (var sector = 0; sector < _options.DirectionSectors; sector++)
        {
            var global = FindEstimate(globalModel.DirectionalEstimates, sector);
            var game = FindEstimate(gameModel.DirectionalEstimates, sector);

            var confidence = Math.Max(
                global?.Confidence ?? 0,
                game?.Confidence ?? 0);

            var error = BlendError(
                global?.MeanErrorRatio,
                game?.MeanErrorRatio,
                game?.SampleCount ?? 0);

            var gain = CalculateGain(error, confidence);

            directionalGains[sector] = gain;
            highestConfidence = Math.Max(highestConfidence, confidence);

            if (Math.Abs(gain - 1.0) > GainEpsilon)
            {
                adjustedEstimateCount++;
            }
        }

        foreach (var band in Enum.GetValues<SpeedBand>())
        {
            var global = FindEstimate(globalModel.SpeedEstimates, band);
            var game = FindEstimate(gameModel.SpeedEstimates, band);

            var confidence = Math.Max(
                global?.Confidence ?? 0,
                game?.Confidence ?? 0);

            var error = BlendError(
                global?.MeanErrorRatio,
                game?.MeanErrorRatio,
                game?.SampleCount ?? 0);

            var gain = CalculateGain(error, confidence);

            speedGains[band] = gain;
            highestConfidence = Math.Max(highestConfidence, confidence);

            if (Math.Abs(gain - 1.0) > GainEpsilon)
            {
                adjustedEstimateCount++;
            }
        }

        var isActionable = adjustedEstimateCount > 0;
        var now = DateTime.UtcNow;

        var profile = new GameProfile
        {
            GameId = gameId,
            BaseSensitivity = _options.BaseSensitivity,
            DirectionalGains = directionalGains,
            SpeedGains = speedGains,
            UpdatedAtUtc = now
        };

        return new OptimizationRecommendation
        {
            GameId = gameId,
            ProposedProfile = profile,
            RequiresApproval = isActionable,
            IsActionable = isActionable,
            Confidence = highestConfidence,
            CreatedAtUtc = now,
            Reason = BuildReason(isActionable, adjustedEstimateCount, highestConfidence)
        };
    }

    private static DirectionalEstimate? FindEstimate(
        IReadOnlyList<DirectionalEstimate> estimates,
        int sector)
    {
        return estimates.FirstOrDefault(x => x.Sector == sector);
    }

    private static SpeedEstimate? FindEstimate(
        IReadOnlyList<SpeedEstimate> estimates,
        SpeedBand band)
    {
        return estimates.FirstOrDefault(x => x.Band == band);
    }

    private double BlendError(
        double? globalError,
        double? gameError,
        int gameSampleCount)
    {
        if (globalError is null)
        {
            return gameError ?? 0;
        }

        if (gameError is null)
        {
            return globalError.Value;
        }

        var gameWeight = gameSampleCount /
            (gameSampleCount + _options.PriorWeight);

        return (globalError.Value * (1 - gameWeight))
             + (gameError.Value * gameWeight);
    }

    private double CalculateGain(double error, double confidence)
    {
        if (confidence < _options.MinimumConfidence)
        {
            return 1.0;
        }

        var correction = -_options.CorrectionStrength * error * confidence;

        correction = Math.Clamp(
            correction,
            -_options.MaximumAdjustment,
            _options.MaximumAdjustment);

        if (Math.Abs(correction) < _options.MinimumActionableAdjustment)
        {
            return 1.0;
        }

        return 1.0 + correction;
    }

    private string BuildReason(
        bool isActionable,
        int adjustedEstimateCount,
        double confidence)
    {
        if (!isActionable)
        {
            return confidence < _options.MinimumConfidence
                ? "Insufficient evidence: no estimate reached the minimum " +
                  $"confidence of {_options.MinimumConfidence:P0}. " +
                  "No adjustments were proposed."
                : "Analysis found no directional or speed bias large enough " +
                  "to justify an adjustment. No changes were proposed.";
        }

        return $"Generated {adjustedEstimateCount} adjustment(s) from global " +
               $"and game-specific performance data (highest confidence " +
               $"{confidence:P0}).";
    }
}
