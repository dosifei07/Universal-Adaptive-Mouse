namespace UAM.Data.Entities;

/// <summary>
/// Persistence-only import provenance. See
/// <see cref="UAM.Core.Models.ImportRecord"/> for the domain shape.
/// </summary>
public sealed class ImportRecordEntity
{
    public string Id { get; set; } = "";

    public string Provider { get; set; } = "";

    public string SourceSessionId { get; set; } = "";

    public string ContentHash { get; set; } = "";

    public string CaptureMethod { get; set; } = "";

    public int ObservationCount { get; set; }

    public DateTime FirstImportedAtUtc { get; set; }

    public DateTime LastImportedAtUtc { get; set; }

    public int ImportCount { get; set; }
}
