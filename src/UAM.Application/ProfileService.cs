using UAM.Core.Models;
using UAM.Core.Serialization;

namespace UAM.Application;

public interface IProfileService
{
    Task<IReadOnlyList<ApprovedProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<ApprovedProfile?> GetProfileAsync(
        string gameId,
        CancellationToken cancellationToken = default);

    Task<ApprovedProfile> SaveEditedProfileAsync(
        string gameId,
        GameProfile editedProfile,
        CancellationToken cancellationToken = default);

    Task<string> ExportAsync(
        string gameId,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and edits profiles that already crossed the approval boundary, and
/// performs the only export path in the application. A pending recommendation
/// has no route to this service.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly TrainingStoreFactory _storeFactory;
    private readonly UamApplicationOptions _options;

    public ProfileService(
        TrainingStoreFactory storeFactory,
        UamApplicationOptions options)
    {
        _storeFactory = storeFactory;
        _options = options;
    }

    public async Task<IReadOnlyList<ApprovedProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetApprovedProfilesAsync(cancellationToken);
    }

    public async Task<ApprovedProfile?> GetProfileAsync(
        string gameId,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetApprovedProfileAsync(gameId, cancellationToken);
    }

    public async Task<ApprovedProfile> SaveEditedProfileAsync(
        string gameId,
        GameProfile editedProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(editedProfile);

        if (!string.Equals(gameId, editedProfile.GameId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The edited profile's game id must match the requested game.",
                nameof(editedProfile));
        }

        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var existing = await store.GetApprovedProfileAsync(gameId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No approved profile exists for '{gameId}'. Approve a " +
                "recommendation before editing the profile.");

        var edited = new ApprovedProfile
        {
            Id = $"prf_{Guid.NewGuid():N}",
            GameId = gameId,
            RecommendationId = existing.RecommendationId,
            Profile = editedProfile with { UpdatedAtUtc = DateTime.UtcNow },
            ApprovedBy = _options.ApprovedBy,
            ApprovedAtUtc = DateTime.UtcNow,
            IsManuallyEdited = true
        };

        await store.SaveApprovedProfileAsync(edited, cancellationToken);
        return edited;
    }

    public async Task<string> ExportAsync(
        string gameId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var approved = await store.GetApprovedProfileAsync(gameId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No approved profile exists for '{gameId}'. A recommendation " +
                "must be approved before it can be exported.");

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            fullPath,
            ProfileJson.Serialize(approved.Profile),
            cancellationToken);

        return fullPath;
    }
}
