using UAM.Core.Models;

namespace UAM.Data.Entities;

/// <summary>
/// Persistence-only shape for a stored recommendation. The proposed profile is
/// stored as canonical JSON so the domain model stays free of EF concerns.
/// </summary>
public sealed class RecommendationEntity
{
    public string Id { get; set; } = "";

    public string GameId { get; set; } = "";

    public RecommendationStatus Status { get; set; }

    public bool IsActionable { get; set; }

    public bool RequiresApproval { get; set; }

    public double Confidence { get; set; }

    public string Reason { get; set; } = "";

    public string ProposedProfileJson { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime StoredAtUtc { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    public string? DecisionNote { get; set; }
}

/// <summary>
/// Persistence-only shape for a profile that crossed the approval boundary.
/// </summary>
public sealed class ApprovedProfileEntity
{
    public string Id { get; set; } = "";

    public string GameId { get; set; } = "";

    public string RecommendationId { get; set; } = "";

    public string ProfileJson { get; set; } = "";

    public string ApprovedBy { get; set; } = "";

    public DateTime ApprovedAtUtc { get; set; }

    public bool IsManuallyEdited { get; set; }
}
