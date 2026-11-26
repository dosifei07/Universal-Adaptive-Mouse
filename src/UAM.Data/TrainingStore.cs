using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UAM.Core.Models;
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

    public ValueTask DisposeAsync()
    {
        return _db.DisposeAsync();
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
