namespace UAM.Core.Models;

/// <summary>
/// A calculated proposal. It is deliberately separate from an approved
/// configuration: nothing may be applied or exported until an explicit
/// approval step has taken place.
/// </summary>
public sealed record OptimizationRecommendation
{
    public required string GameId { get; init; }

    public required GameProfile ProposedProfile { get; init; }

    /// <summary>
    /// True when the recommendation contains changes that need explicit
    /// approval. False when no actionable adjustment was produced.
    /// </summary>
    public bool RequiresApproval { get; init; } = true;

    /// <summary>
    /// False when there is not enough confident evidence to justify a change.
    /// A non-actionable recommendation is informational only.
    /// </summary>
    public bool IsActionable { get; init; }

    /// <summary>
    /// Highest confidence among the estimates that were considered.
    /// </summary>
    public double Confidence { get; init; }

    public string Reason { get; init; } = "";

    public DateTime CreatedAtUtc { get; init; }
}
