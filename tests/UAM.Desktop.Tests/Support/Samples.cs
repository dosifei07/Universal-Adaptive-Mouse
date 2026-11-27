using UAM.Application;
using UAM.Core.Models;
using UAM.Core.Validation;

namespace UAM.Desktop.Tests;

/// <summary>Builders for realistic domain objects used across the tests.</summary>
internal static class Samples
{
    public static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    public static GameProfile Profile(
        string gameId = "valorant",
        double sector0Gain = 0.98,
        double dpi = 800,
        double inGameSensitivity = 0.4)
    {
        return new GameProfile
        {
            GameId = gameId,
            Dpi = dpi,
            InGameSensitivity = inGameSensitivity,
            DirectionalGains = Enumerable.Range(0, 8).ToDictionary(
                sector => sector,
                sector => sector == 0 ? sector0Gain : 1.0),
            SpeedGains = new Dictionary<SpeedBand, double>
            {
                [SpeedBand.Slow] = 1.0,
                [SpeedBand.Medium] = 0.99,
                [SpeedBand.Fast] = 1.0
            },
            UpdatedAtUtc = Now
        };
    }

    public static OptimizationRecommendation Recommendation(
        string gameId = "valorant",
        bool actionable = true)
    {
        return new OptimizationRecommendation
        {
            GameId = gameId,
            ProposedProfile = Profile(gameId),
            IsActionable = actionable,
            RequiresApproval = actionable,
            Confidence = actionable ? 0.62 : 0.0,
            Reason = actionable ? "Consistent rightward overshoot." : "Not enough evidence.",
            CreatedAtUtc = Now
        };
    }

    public static StoredRecommendation Stored(
        string id,
        RecommendationStatus status = RecommendationStatus.Pending,
        string gameId = "valorant",
        bool actionable = true,
        int minutesAgo = 0)
    {
        return new StoredRecommendation
        {
            Id = id,
            Recommendation = Recommendation(gameId, actionable),
            Status = status,
            StoredAtUtc = Now.AddMinutes(-minutesAgo),
            DecidedAtUtc = status is RecommendationStatus.Pending or RecommendationStatus.Informational
                ? null
                : Now
        };
    }

    public static ApprovedProfile Approved(
        string id,
        string gameId = "valorant",
        int minutesAgo = 0,
        bool edited = false,
        double sector0Gain = 0.98)
    {
        return new ApprovedProfile
        {
            Id = id,
            GameId = gameId,
            RecommendationId = "rec_" + id,
            Profile = Profile(gameId, sector0Gain),
            ApprovedBy = "tester",
            ApprovedAtUtc = Now.AddMinutes(-minutesAgo),
            IsManuallyEdited = edited
        };
    }

    public static DirectionalEstimate Direction(
        int sector,
        double error,
        int samples = 200,
        double confidence = 0.8)
    {
        return new DirectionalEstimate
        {
            Sector = sector,
            SampleCount = samples,
            MeanErrorRatio = error,
            Confidence = confidence
        };
    }

    public static SpeedEstimate Speed(
        SpeedBand band,
        double error,
        int samples = 200,
        double confidence = 0.8)
    {
        return new SpeedEstimate
        {
            Band = band,
            SampleCount = samples,
            MeanErrorRatio = error,
            Confidence = confidence
        };
    }

    public static DashboardSummary EmptyDashboard() => new()
    {
        DirectionalEstimates = [],
        SpeedEstimates = []
    };

    public static DashboardSummary Dashboard(
        StoredRecommendation? pending = null,
        ApprovedProfile? active = null) => new()
    {
        SessionCount = 3,
        ObservationCount = 1200,
        GameCount = 2,
        ProfileCount = active is null ? 0 : 1,
        PendingApprovalCount = pending is null ? 0 : 1,
        LastObservationUtc = Now,
        DirectionalEstimates = [Direction(0, 0.10), Direction(4, -0.06)],
        SpeedEstimates = [Speed(SpeedBand.Medium, 0.04)],
        PendingRecommendation = pending,
        ActiveProfile = active
    };

    public static AnalysisResult Analysis(
        string? gameId = "valorant",
        IReadOnlyList<string>? games = null,
        IReadOnlyList<RejectedObservation>? rejections = null,
        bool hasData = true)
    {
        var model = hasData
            ? new PlayerModel
            {
                PlayerId = "player-001",
                DirectionalEstimates = [Direction(0, 0.12), Direction(2, -0.03)],
                SpeedEstimates = [Speed(SpeedBand.Slow, 0.02), Speed(SpeedBand.Fast, 0.09)]
            }
            : PlayerModel.Empty("player-001");

        return new AnalysisResult
        {
            GlobalModel = model,
            GameModel = model,
            GameId = gameId,
            TotalObservations = hasData ? 410 : 0,
            AcceptedCount = hasData ? 400 : 0,
            RejectedCount = rejections?.Count ?? 0,
            Rejections = rejections ?? [],
            AvailableGames = games ?? (hasData ? ["valorant", "cs2"] : [])
        };
    }

    public static RejectedObservation Rejected(params ObservationValidationCode[] codes)
    {
        return new RejectedObservation(
            new TrainingObservation
            {
                SessionId = "s",
                GameId = "valorant",
                ScenarioId = "sc",
                TimestampUtc = Now
            },
            new ObservationValidationResult { Codes = codes });
    }

    public static TrainingSessionSummary Session(
        string id,
        string gameId = "valorant",
        string scenarioId = "gridshot",
        int minutesAgo = 0,
        int observations = 100,
        int durationMinutes = 5)
    {
        var last = Now.AddMinutes(-minutesAgo);

        return new TrainingSessionSummary
        {
            SessionId = id,
            GameId = gameId,
            ScenarioId = scenarioId,
            FirstObservationUtc = last.AddMinutes(-durationMinutes),
            LastObservationUtc = last,
            ObservationCount = observations
        };
    }
}
