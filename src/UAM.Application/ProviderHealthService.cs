using UAM.Core.Models;

namespace UAM.Application;

/// <summary>
/// Honest integration health. "Connected" means UAM has actually stored data
/// from that provider; "Ready" means working code exists but no data has
/// arrived yet. Nothing is ever marked connected merely because it is planned.
/// </summary>
public enum ProviderHealth
{
    /// <summary>Working code is present; no data imported yet.</summary>
    Ready,

    /// <summary>Working code is present and data has been imported.</summary>
    Connected,

    /// <summary>Research or planned; no integration is shipped.</summary>
    NotConnected,

    /// <summary>The provider exists but cannot be reached or read.</summary>
    Unavailable,

    /// <summary>Terms do not permit integration; no code exists.</summary>
    Blocked
}

public sealed record ProviderStatusSnapshot
{
    public required ProviderDefinition Provider { get; init; }

    public required ProviderHealth Health { get; init; }

    public required string Detail { get; init; }
}

public interface IProviderHealthService
{
    Task<IReadOnlyList<ProviderStatusSnapshot>> GetStatusesAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ProviderHealthService : IProviderHealthService
{
    private readonly TrainingStoreFactory _storeFactory;

    public ProviderHealthService(TrainingStoreFactory storeFactory)
    {
        _storeFactory = storeFactory
            ?? throw new ArgumentNullException(nameof(storeFactory));
    }

    public async Task<IReadOnlyList<ProviderStatusSnapshot>> GetStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        var imports = await store.GetImportRecordsAsync(
            cancellationToken: cancellationToken);

        var importedProviders = imports
            .Select(x => x.Provider)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ProviderRegistry.All
            .Select(provider => Describe(provider, importedProviders.Contains(provider.Id)))
            .ToList();
    }

    private static ProviderStatusSnapshot Describe(
        ProviderDefinition provider,
        bool hasImportedData)
    {
        if (provider.Terms == TermsPosition.NotPermitted)
        {
            return new ProviderStatusSnapshot
            {
                Provider = provider,
                Health = ProviderHealth.Blocked,
                Detail = provider.TermsNote
            };
        }

        if (provider.Maturity != ProviderMaturity.Available)
        {
            return new ProviderStatusSnapshot
            {
                Provider = provider,
                Health = ProviderHealth.NotConnected,
                Detail = provider.Description
            };
        }

        return new ProviderStatusSnapshot
        {
            Provider = provider,
            Health = hasImportedData
                ? ProviderHealth.Connected
                : ProviderHealth.Ready,
            Detail = hasImportedData
                ? "Data from this provider has been imported."
                : provider.Description
        };
    }
}
