using UAM.Core.Models;

namespace UAM.Application;

public interface IHistoryService
{
    Task<IReadOnlyList<TrainingSessionSummary>> GetSessionsAsync(
        string? gameId = null,
        string? scenarioId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetGamesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetScenariosAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only access to stored training history with game and scenario filters.
/// </summary>
public sealed class HistoryService : IHistoryService
{
    private readonly TrainingStoreFactory _storeFactory;

    public HistoryService(TrainingStoreFactory storeFactory)
    {
        _storeFactory = storeFactory;
    }

    public async Task<IReadOnlyList<TrainingSessionSummary>> GetSessionsAsync(
        string? gameId = null,
        string? scenarioId = null,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetSessionsAsync(gameId, scenarioId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetGamesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetGameIdsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetScenariosAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetScenarioIdsAsync(gameId, cancellationToken);
    }
}
