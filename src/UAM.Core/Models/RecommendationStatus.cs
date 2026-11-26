namespace UAM.Core.Models;

/// <summary>
/// Lifecycle of a stored optimization recommendation. Only an
/// <see cref="Approved"/> recommendation may produce an approved profile.
/// </summary>
public enum RecommendationStatus
{
    /// <summary>Awaiting a user decision.</summary>
    Pending,

    /// <summary>Approved by the user; an approved profile was created.</summary>
    Approved,

    /// <summary>Explicitly rejected by the user.</summary>
    Rejected,

    /// <summary>Replaced by a newer recommendation for the same game.</summary>
    Superseded,

    /// <summary>Not actionable; retained for transparency only.</summary>
    Informational
}
