namespace UAM.Core.Models;

/// <summary>
/// How complete a provider integration is. Only <see cref="Available"/> means
/// this build contains working integration code for the provider.
/// </summary>
public enum ProviderMaturity
{
    /// <summary>Working today in this build.</summary>
    Available,

    /// <summary>Being researched; no integration code is shipped.</summary>
    Research,

    /// <summary>Intended for a later release.</summary>
    Planned
}

/// <summary>
/// The provider's (or game's) documented position on third-party data access.
/// This is recorded before any connector work starts.
/// </summary>
public enum TermsPosition
{
    /// <summary>Terms explicitly permit the integration.</summary>
    Permitted,

    /// <summary>Permission has been requested or the terms are ambiguous.</summary>
    PermissionRequested,

    /// <summary>Terms prohibit the integration. No integration code may exist.</summary>
    NotPermitted
}

/// <summary>
/// One training-data source known to UAM. A provider is only importable when
/// it is both <see cref="ProviderMaturity.Available"/> and
/// <see cref="TermsPosition.Permitted"/>; a "not permitted" entry never gets
/// integration code.
/// </summary>
public sealed record ProviderDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required ProviderMaturity Maturity { get; init; }

    public required TermsPosition Terms { get; init; }

    /// <summary>A short, factual explanation of the terms record.</summary>
    public required string TermsNote { get; init; }

    public required string Description { get; init; }

    /// <summary>
    /// True only when real code exists and the terms permit using it.
    /// </summary>
    public bool CanIntegrate =>
        Maturity == ProviderMaturity.Available &&
        Terms == TermsPosition.Permitted;
}

/// <summary>
/// The single source of truth for provider identity and terms. UI text is
/// derived from these records so the app cannot claim a connection that the
/// registry does not permit.
/// </summary>
public static class ProviderRegistry
{
    public const string UamLab = "uam-lab";
    public const string CanonicalJson = "canonical-json";
    public const string Aimlabs = "aimlabs";
    public const string Other = "other";
    public const string RiotTelemetry = "riot-telemetry";

    public static IReadOnlyList<ProviderDefinition> All { get; } =
    [
        new ProviderDefinition
        {
            Id = UamLab,
            Name = "UAM Lab",
            Maturity = ProviderMaturity.Available,
            Terms = TermsPosition.Permitted,
            TermsNote = "First-party capture: UAM records pointer input itself.",
            Description =
                "A built-in 2D pointer task. Completed sessions are imported " +
                "into UAM automatically with no manual JSON handling. This is " +
                "a pointer task, not first-person camera aiming."
        },
        new ProviderDefinition
        {
            Id = CanonicalJson,
            Name = "Training JSON import",
            Maturity = ProviderMaturity.Available,
            Terms = TermsPosition.Permitted,
            TermsNote = "UAM's own canonical document format.",
            Description =
                "Import a versioned, provider-neutral UAM training document " +
                "from a file or a watched folder. Re-importing the same " +
                "content is detected and creates no duplicate observations."
        },
        new ProviderDefinition
        {
            Id = Aimlabs,
            Name = "Aimlabs",
            Maturity = ProviderMaturity.Research,
            Terms = TermsPosition.PermissionRequested,
            TermsNote =
                "No documented telemetry export; permission has not been granted.",
            Description =
                "Researching how Aimlabs session data could be exported into " +
                "the canonical format. UAM is not connected to Aimlabs and " +
                "reads no data from it."
        },
        new ProviderDefinition
        {
            Id = Other,
            Name = "Other training providers",
            Maturity = ProviderMaturity.Planned,
            Terms = TermsPosition.PermissionRequested,
            TermsNote =
                "Each provider needs its own terms review before integration.",
            Description =
                "Any provider that can produce a permitted canonical training " +
                "document can be added in a later release."
        },
        new ProviderDefinition
        {
            Id = RiotTelemetry,
            Name = "Riot Games telemetry",
            Maturity = ProviderMaturity.Planned,
            Terms = TermsPosition.NotPermitted,
            TermsNote =
                "Riot's terms do not permit this integration; no code exists.",
            Description =
                "Recorded as not permitted. UAM will not read game telemetry " +
                "or build an integration for this source."
        }
    ];

    public static ProviderDefinition? Find(string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(
                x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static ProviderDefinition Get(string id)
    {
        return Find(id)
            ?? throw new KeyNotFoundException(
                $"Unknown provider '{id}'. Known providers: " +
                string.Join(", ", All.Select(x => x.Id)) + ".");
    }

    public static bool CanIntegrate(string id)
    {
        return Find(id)?.CanIntegrate ?? false;
    }
}
