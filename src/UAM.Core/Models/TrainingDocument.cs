using System.Text.Json;

namespace UAM.Core.Models;

/// <summary>
/// Version information for the canonical training document format. Providers
/// convert their data into this format; the format, not the provider, decides
/// how UAM stores and analyses observations.
/// </summary>
public static class TrainingDocumentFormat
{
    public const string CurrentSchemaVersion = "1.0";

    public const int SupportedMajorVersion = 1;
}

/// <summary>
/// A versioned, provenance-carrying envelope around provider-neutral training
/// observations. <see cref="Capture"/> is an optional provider-specific payload
/// (for example UAM Lab's raw pointer samples); UAM's analysers only consume
/// <see cref="Observations"/>.
/// </summary>
public sealed record TrainingDocument
{
    public string SchemaVersion { get; init; } =
        TrainingDocumentFormat.CurrentSchemaVersion;

    /// <summary>Registry id of the provider that produced the data.</summary>
    public required string Provider { get; init; }

    /// <summary>How the data was captured, e.g. "json-import" or "2d-canvas".</summary>
    public required string CaptureMethod { get; init; }

    /// <summary>The provider's session identifier. Part of the import identity.</summary>
    public required string SourceSessionId { get; init; }

    public required IReadOnlyList<TrainingObservation> Observations { get; init; }

    /// <summary>Optional raw capture payload preserved for provenance.</summary>
    public JsonElement? Capture { get; init; }
}
