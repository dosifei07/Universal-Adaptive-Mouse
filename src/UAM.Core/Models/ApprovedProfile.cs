namespace UAM.Core.Models;

/// <summary>
/// A profile that has crossed the approval boundary and may be exported. The
/// application layer is the only producer of this record.
/// </summary>
public sealed record ApprovedProfile
{
    public required string Id { get; init; }

    public required string GameId { get; init; }

    public required string RecommendationId { get; init; }

    public required GameProfile Profile { get; init; }

    public string ApprovedBy { get; init; } = "local-user";

    public DateTime ApprovedAtUtc { get; init; }

    /// <summary>
    /// True when the user edited the profile after approval rather than
    /// accepting the recommendation verbatim.
    /// </summary>
    public bool IsManuallyEdited { get; init; }
}
