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
    }
}
