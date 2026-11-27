namespace UAM.Core.Models;

/// <summary>Result of trying to import a training document.</summary>
public enum ImportOutcome
{
    /// <summary>The document was new and its observations were stored.</summary>
    Imported,

    /// <summary>The same provider session and content were seen before.</summary>
    Duplicate,

    /// <summary>The document parsed but validation rejected every observation.</summary>
    Rejected
}

/// <summary>
/// Provenance and idempotency metadata for one imported document. The
/// combination of provider, source session and content hash identifies an
/// import; a repeated import only updates <see cref="LastImportedAtUtc"/> and
/// <see cref="ImportCount"/>.
/// </summary>
public sealed record ImportRecord
{
    public required string Id { get; init; }

    public required string Provider { get; init; }

    public required string SourceSessionId { get; init; }

    /// <summary>Lower-case SHA-256 of the exact document content.</summary>
    public required string ContentHash { get; init; }

    public required string CaptureMethod { get; init; }

    public int ObservationCount { get; init; }

    public DateTime FirstImportedAtUtc { get; init; }

    public DateTime LastImportedAtUtc { get; init; }

    /// <summary>How many times this exact content was offered to UAM.</summary>
    public int ImportCount { get; init; } = 1;
}
