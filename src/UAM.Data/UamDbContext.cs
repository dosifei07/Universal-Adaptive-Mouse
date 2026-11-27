using Microsoft.EntityFrameworkCore;
using UAM.Data.Entities;

namespace UAM.Data;

public sealed class UamDbContext : DbContext
{
    public UamDbContext(DbContextOptions<UamDbContext> options)
        : base(options)
    {
    }

    public DbSet<TrainingSessionEntity> Sessions => Set<TrainingSessionEntity>();

    public DbSet<TrainingObservationEntity> Observations =>
        Set<TrainingObservationEntity>();

    public DbSet<RecommendationEntity> Recommendations =>
        Set<RecommendationEntity>();

    public DbSet<ApprovedProfileEntity> ApprovedProfiles =>
        Set<ApprovedProfileEntity>();

    public DbSet<ImportRecordEntity> ImportRecords =>
        Set<ImportRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var session = modelBuilder.Entity<TrainingSessionEntity>();
        session.ToTable("TrainingSessions");
        session.HasKey(x => new { x.SessionId, x.GameId });
        session.Property(x => x.SessionId).HasMaxLength(200);
        session.Property(x => x.GameId).HasMaxLength(200);
        session.Property(x => x.ScenarioId).HasMaxLength(200);
        session.HasIndex(x => x.LastObservationUtc);
        session.HasIndex(x => x.GameId);

        var observation = modelBuilder.Entity<TrainingObservationEntity>();
        observation.ToTable("TrainingObservations");
        observation.HasKey(x => x.Id);
        observation.Property(x => x.SessionId).HasMaxLength(200);
        observation.Property(x => x.GameId).HasMaxLength(200);
        observation.Property(x => x.ScenarioId).HasMaxLength(200);
        observation.Property(x => x.MovementKind).HasConversion<int>();
        observation.HasIndex(x => new { x.SessionId, x.GameId });
        observation.HasIndex(x => new { x.GameId, x.TimestampUtc });
        observation
            .HasOne(x => x.Session)
            .WithMany(x => x.Observations)
            .HasForeignKey(x => new { x.SessionId, x.GameId })
            .OnDelete(DeleteBehavior.Cascade);

        var recommendation = modelBuilder.Entity<RecommendationEntity>();
        recommendation.ToTable("Recommendations");
        recommendation.HasKey(x => x.Id);
        recommendation.Property(x => x.Id).HasMaxLength(64);
        recommendation.Property(x => x.GameId).HasMaxLength(200);
        recommendation.Property(x => x.Status).HasConversion<int>();
        recommendation.Property(x => x.Reason).HasMaxLength(1000);
        recommendation.HasIndex(x => new { x.GameId, x.Status });
        recommendation.HasIndex(x => x.StoredAtUtc);

        var approvedProfile = modelBuilder.Entity<ApprovedProfileEntity>();
        approvedProfile.ToTable("ApprovedProfiles");
        approvedProfile.HasKey(x => x.Id);
        approvedProfile.Property(x => x.Id).HasMaxLength(64);
        approvedProfile.Property(x => x.GameId).HasMaxLength(200);
        approvedProfile.Property(x => x.RecommendationId).HasMaxLength(64);
        approvedProfile.Property(x => x.ApprovedBy).HasMaxLength(200);
        approvedProfile.HasIndex(x => x.GameId);
        approvedProfile.HasIndex(x => x.ApprovedAtUtc);

        var importRecord = modelBuilder.Entity<ImportRecordEntity>();
        importRecord.ToTable("ImportRecords");
        importRecord.HasKey(x => x.Id);
        importRecord.Property(x => x.Id).HasMaxLength(64);
        importRecord.Property(x => x.Provider).HasMaxLength(200);
        importRecord.Property(x => x.SourceSessionId).HasMaxLength(200);
        importRecord.Property(x => x.ContentHash).HasMaxLength(64);
        importRecord.Property(x => x.CaptureMethod).HasMaxLength(100);
        importRecord
            .HasIndex(x => new
            {
                x.Provider,
                x.SourceSessionId,
                x.ContentHash
            })
            .IsUnique();
        importRecord.HasIndex(x => x.ContentHash);
        importRecord.HasIndex(x => x.LastImportedAtUtc);
    }
}
