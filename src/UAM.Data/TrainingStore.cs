using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UAM.Core.Models;
using UAM.Core.Serialization;
using UAM.Core.Validation;
using UAM.Data.Entities;

namespace UAM.Data;

public sealed record TrainingStoreSaveResult
{
    public int SavedCount { get; init; }

    public int RejectedCount { get; init; }

    public required IReadOnlyList<RejectedObservation> Rejections { get; init; }
}

/// <summary>
/// Aggregate counts used by the dashboard. These are read from SQLite; they
/// are never fabricated in the UI.
/// </summary>
public sealed record TrainingDataCounts
{
    public int SessionCount { get; init; }

    public int ObservationCount { get; init; }

    public int GameCount { get; init; }

    public int PendingRecommendationCount { get; init; }

    public int ApprovedProfileCount { get; init; }

    public DateTime? LastObservationUtc { get; init; }
}

/// <summary>
/// Local SQLite persistence for training sessions. Validation happens before
/// rows are written, so analysis never has to defend against corrupt storage.
/// </summary>
public sealed class TrainingStore : IAsyncDisposable
{
    private readonly UamDbContext _db;
    private readonly ObservationValidator _validator;

    public TrainingStore(string databasePath, ObservationValidator? validator = null)
        : this(CreateOptions(databasePath), validator)
    {
    }

    public TrainingStore(
        DbContextOptions<UamDbContext> options,
        ObservationValidator? validator = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _db = new UamDbContext(options);
        _validator = validator ?? new ObservationValidator();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task<TrainingStoreSaveResult> SaveObservationsAsync(
        IEnumerable<TrainingObservation> observations,
        CancellationToken cancellationToken = default)
    {
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

        foreach (var group in accepted.GroupBy(
                     x => new { x.SessionId, x.GameId }))
        {
            var sessionId = group.Key.SessionId;
            var gameId = group.Key.GameId;

            var session = await _db.Sessions.FindAsync(
                [sessionId, gameId],
                cancellationToken);

            var first = group.Min(x => x.TimestampUtc);
            var last = group.Max(x => x.TimestampUtc);

            if (session is null)
            {
                session = new TrainingSessionEntity
                {
                    SessionId = sessionId,
                    GameId = gameId,
                    ScenarioId = group.First().ScenarioId,
                    FirstObservationUtc = first,
                    LastObservationUtc = last,
                    ObservationCount = group.Count()
                };

                _db.Sessions.Add(session);
            }
            else
            {
                session.FirstObservationUtc =
                    first < session.FirstObservationUtc
                        ? first
                        : session.FirstObservationUtc;

                session.LastObservationUtc =
                    last > session.LastObservationUtc
                        ? last
                        : session.LastObservationUtc;

                session.ObservationCount += group.Count();
            }

            foreach (var observation in group)
            {
                _db.Observations.Add(new TrainingObservationEntity
                {
                    SessionId = observation.SessionId,
                    GameId = observation.GameId,
                    ScenarioId = observation.ScenarioId,
                    TimestampUtc = observation.TimestampUtc,
                    DirectionDegrees = observation.DirectionDegrees,
                    SpeedPixelsPerSecond = observation.SpeedPixelsPerSecond,
                    SignedErrorRatio = observation.SignedErrorRatio,
                    AcquisitionTimeMs = observation.AcquisitionTimeMs,
                    Hit = observation.Hit,
                    MovementKind = observation.MovementKind
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new TrainingStoreSaveResult
        {
            SavedCount = accepted.Count,
            RejectedCount = rejections.Count,
            Rejections = rejections
        };
    }

    public async Task<IReadOnlyList<TrainingObservation>> LoadObservationsAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Observations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            query = query.Where(x => x.GameId == gameId);
        }

        return await query
            .OrderBy(x => x.TimestampUtc)
            .ThenBy(x => x.Id)
            .Select(x => new TrainingObservation
            {
                SessionId = x.SessionId,
                GameId = x.GameId,
                ScenarioId = x.ScenarioId,
                TimestampUtc = x.TimestampUtc,
                DirectionDegrees = x.DirectionDegrees,
                SpeedPixelsPerSecond = x.SpeedPixelsPerSecond,
                SignedErrorRatio = x.SignedErrorRatio,
                AcquisitionTimeMs = x.AcquisitionTimeMs,
                Hit = x.Hit,
                MovementKind = x.MovementKind
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TrainingSessionSummary>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Sessions
            .AsNoTracking()
            .OrderByDescending(x => x.LastObservationUtc)
            .Select(x => new TrainingSessionSummary
            {
                SessionId = x.SessionId,
                GameId = x.GameId,
                ScenarioId = x.ScenarioId,
                FirstObservationUtc = x.FirstObservationUtc,
                LastObservationUtc = x.LastObservationUtc,
                ObservationCount = x.ObservationCount
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TrainingSessionSummary>> GetSessionsAsync(
        string? gameId,
        string? scenarioId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Sessions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            query = query.Where(x => x.GameId == gameId);
        }

        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            query = query.Where(x => x.ScenarioId == scenarioId);
        }

        return await query
            .OrderByDescending(x => x.LastObservationUtc)
            .Select(x => new TrainingSessionSummary
            {
                SessionId = x.SessionId,
                GameId = x.GameId,
                ScenarioId = x.ScenarioId,
                FirstObservationUtc = x.FirstObservationUtc,
                LastObservationUtc = x.LastObservationUtc,
                ObservationCount = x.ObservationCount
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetGameIdsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Sessions
            .AsNoTracking()
            .Select(x => x.GameId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetScenarioIdsAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Sessions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            query = query.Where(x => x.GameId == gameId);
        }

        return await query
            .Select(x => x.ScenarioId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public async Task<TrainingDataCounts> GetDataCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var sessionCount = await _db.Sessions.CountAsync(cancellationToken);
        var observationCount = await _db.Observations.CountAsync(cancellationToken);
        var gameCount = await _db.Sessions
            .Select(x => x.GameId)
            .Distinct()
            .CountAsync(cancellationToken);
        var pendingCount = await _db.Recommendations
            .CountAsync(
                x => x.Status == RecommendationStatus.Pending,
                cancellationToken);
        var approvedProfileCount = await _db.ApprovedProfiles
            .Select(x => x.GameId)
            .Distinct()
            .CountAsync(cancellationToken);

        DateTime? lastObservation = await _db.Observations
            .OrderByDescending(x => x.TimestampUtc)
            .Select(x => (DateTime?)x.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new TrainingDataCounts
        {
            SessionCount = sessionCount,
            ObservationCount = observationCount,
            GameCount = gameCount,
            PendingRecommendationCount = pendingCount,
            ApprovedProfileCount = approvedProfileCount,
            LastObservationUtc = lastObservation
        };
    }

    public async Task SaveRecommendationAsync(
        StoredRecommendation recommendation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        var entity = new RecommendationEntity
        {
            Id = recommendation.Id,
            GameId = recommendation.Recommendation.GameId,
            Status = recommendation.Status,
            IsActionable = recommendation.Recommendation.IsActionable,
            RequiresApproval = recommendation.Recommendation.RequiresApproval,
            Confidence = recommendation.Recommendation.Confidence,
            Reason = recommendation.Recommendation.Reason,
            ProposedProfileJson =
                ProfileJson.Serialize(recommendation.Recommendation.ProposedProfile),
            CreatedAtUtc = recommendation.Recommendation.CreatedAtUtc,
            StoredAtUtc = recommendation.StoredAtUtc,
            DecidedAtUtc = recommendation.DecidedAtUtc,
            DecisionNote = recommendation.DecisionNote
        };

        _db.Recommendations.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredRecommendation>> GetRecommendationsAsync(
        RecommendationStatus? status = null,
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Recommendations.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            query = query.Where(x => x.GameId == gameId);
        }

        var entities = await query
            .OrderByDescending(x => x.StoredAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<StoredRecommendation?> GetRecommendationAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var entity = await _db.Recommendations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<StoredRecommendation?> UpdateRecommendationStatusAsync(
        string id,
        RecommendationStatus status,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var entity = await _db.Recommendations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        entity.Status = status;
        entity.DecidedAtUtc = DateTime.UtcNow;
        entity.DecisionNote = note;

        await _db.SaveChangesAsync(cancellationToken);

        return ToDomain(entity);
    }

    public async Task SaveApprovedProfileAsync(
        ApprovedProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _db.ApprovedProfiles.Add(new ApprovedProfileEntity
        {
            Id = profile.Id,
            GameId = profile.GameId,
            RecommendationId = profile.RecommendationId,
            ProfileJson = ProfileJson.Serialize(profile.Profile),
            ApprovedBy = profile.ApprovedBy,
            ApprovedAtUtc = profile.ApprovedAtUtc,
            IsManuallyEdited = profile.IsManuallyEdited
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovedProfile>> GetApprovedProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _db.ApprovedProfiles
            .AsNoTracking()
            .OrderByDescending(x => x.ApprovedAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<ApprovedProfile?> GetApprovedProfileAsync(
        string gameId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var entity = await _db.ApprovedProfiles
            .AsNoTracking()
            .Where(x => x.GameId == gameId)
            .OrderByDescending(x => x.ApprovedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public ValueTask DisposeAsync()
    {
        return _db.DisposeAsync();
    }

    private static StoredRecommendation ToDomain(RecommendationEntity entity)
    {
        return new StoredRecommendation
        {
            Id = entity.Id,
            Status = entity.Status,
            StoredAtUtc = entity.StoredAtUtc,
            DecidedAtUtc = entity.DecidedAtUtc,
            DecisionNote = entity.DecisionNote,
            Recommendation = new OptimizationRecommendation
            {
                GameId = entity.GameId,
                ProposedProfile = ProfileJson.Deserialize(entity.ProposedProfileJson),
                IsActionable = entity.IsActionable,
                RequiresApproval = entity.RequiresApproval,
                Confidence = entity.Confidence,
                Reason = entity.Reason,
                CreatedAtUtc = entity.CreatedAtUtc
            }
        };
    }

    private static ApprovedProfile ToDomain(ApprovedProfileEntity entity)
    {
        return new ApprovedProfile
        {
            Id = entity.Id,
            GameId = entity.GameId,
            RecommendationId = entity.RecommendationId,
            Profile = ProfileJson.Deserialize(entity.ProfileJson),
            ApprovedBy = entity.ApprovedBy,
            ApprovedAtUtc = entity.ApprovedAtUtc,
            IsManuallyEdited = entity.IsManuallyEdited
        };
    }

    private static DbContextOptions<UamDbContext> CreateOptions(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath
        }.ToString();

        return new DbContextOptionsBuilder<UamDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }
}
