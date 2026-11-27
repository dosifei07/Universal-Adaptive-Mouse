using System.Text.Json;
using UAM.Core.Models;
using UAM.Core.Serialization;

namespace UAM.Application;

/// <summary>Thrown when a provider's terms position forbids integration.</summary>
public sealed class ProviderNotPermittedException : InvalidOperationException
{
    public ProviderNotPermittedException(ProviderDefinition provider)
        : base(
            $"Provider '{provider.Name}' is not importable: {provider.Terms}. " +
            provider.TermsNote)
    {
        Provider = provider;
    }

    public ProviderDefinition Provider { get; }
}

public sealed record ImportResult
{
    public required ImportOutcome Outcome { get; init; }

    public required TrainingDocument Document { get; init; }

    /// <summary>The stored provenance record; null when nothing was stored.</summary>
    public ImportRecord? Record { get; init; }

    public int SavedCount { get; init; }

    public int RejectedCount { get; init; }

    public string Detail { get; init; } = string.Empty;

    public bool IsDuplicate => Outcome == ImportOutcome.Duplicate;
}

public interface IImportService
{
    Task<ImportResult> ImportFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ImportResult> ImportContentAsync(
        string content,
        CancellationToken cancellationToken = default);

    Task<ImportResult> ImportDocumentAsync(
        TrainingDocument document,
        string contentHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportRecord>> GetImportsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Imports canonical training documents with provenance and idempotency.
///
/// The identity of an import is provider + source session + SHA-256 content
/// hash. Seeing the same content again only updates attempt metadata; it never
/// writes observations twice. Provider terms are checked before any data is
/// stored, so a "not permitted" source cannot be imported at all.
/// </summary>
public sealed class ImportService : IImportService
{
    private readonly TrainingStoreFactory _storeFactory;

    public ImportService(TrainingStoreFactory storeFactory)
    {
        _storeFactory = storeFactory
            ?? throw new ArgumentNullException(nameof(storeFactory));
    }

    public async Task<ImportResult> ImportFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        return await ImportContentAsync(content, cancellationToken);
    }

    public async Task<ImportResult> ImportContentAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var document = TrainingDocumentJson.Deserialize(content);
        return await ImportDocumentAsync(
            document,
            TrainingDocumentJson.ComputeContentHash(content),
            cancellationToken);
    }

    public async Task<ImportResult> ImportDocumentAsync(
        TrainingDocument document,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var provider = ProviderRegistry.Find(document.Provider)
            ?? throw new InvalidOperationException(
                $"Unknown provider '{document.Provider}'.");

        if (!provider.CanIntegrate)
        {
            throw new ProviderNotPermittedException(provider);
        }

        var now = DateTime.UtcNow;

        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var existing = await store.FindImportRecordAsync(
                provider.Id,
                document.SourceSessionId,
                contentHash,
                cancellationToken)
            ?? await store.FindImportRecordBySessionAsync(
                provider.Id,
                document.SourceSessionId,
                cancellationToken)
            ?? await store.FindImportRecordByHashAsync(
                contentHash,
                cancellationToken);

        if (existing is not null)
        {
            var touched = await store.TouchImportRecordAsync(
                existing.Id,
                now,
                cancellationToken) ?? existing;

            return new ImportResult
            {
                Outcome = ImportOutcome.Duplicate,
                Document = document,
                Record = touched,
                Detail =
                    $"This {provider.Name} document was already imported. " +
                    "No observations were written again."
            };
        }

        var save = await store.SaveObservationsAsync(
            document.Observations,
            cancellationToken);

        if (save.SavedCount == 0)
        {
            return new ImportResult
            {
                Outcome = ImportOutcome.Rejected,
                Document = document,
                SavedCount = 0,
                RejectedCount = save.RejectedCount,
                Detail =
                    "No observation in the document passed validation, so " +
                    "nothing was stored."
            };
        }

        var record = await store.AddImportRecordAsync(
            new ImportRecord
            {
                Id = $"imp_{Guid.NewGuid():N}",
                Provider = provider.Id,
                SourceSessionId = document.SourceSessionId,
                ContentHash = contentHash,
                CaptureMethod = document.CaptureMethod,
                ObservationCount = save.SavedCount,
                FirstImportedAtUtc = now,
                LastImportedAtUtc = now,
                ImportCount = 1
            },
            cancellationToken);

        return new ImportResult
        {
            Outcome = ImportOutcome.Imported,
            Document = document,
            Record = record,
            SavedCount = save.SavedCount,
            RejectedCount = save.RejectedCount,
            Detail =
                $"Imported {save.SavedCount} observation(s) from " +
                $"{provider.Name}."
        };
    }

    public async Task<IReadOnlyList<ImportRecord>> GetImportsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetImportRecordsAsync(
            cancellationToken: cancellationToken);
    }

    internal static bool IsParseFailure(Exception exception)
    {
        return exception is JsonException or TrainingDocumentVersionException;
    }
}
