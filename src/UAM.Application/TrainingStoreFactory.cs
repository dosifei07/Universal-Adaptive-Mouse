using UAM.Data;

namespace UAM.Application;

/// <summary>
/// Creates short-lived, initialized stores. A store owns a DbContext, so
/// services create one per operation instead of sharing a context across
/// threads or UI interactions.
/// </summary>
public sealed class TrainingStoreFactory
{
    public TrainingStoreFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public async Task<TrainingStore> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var store = new TrainingStore(DatabasePath);
        try
        {
            await store.InitializeAsync(cancellationToken);
            return store;
        }
        catch
        {
            await store.DisposeAsync();
            throw;
        }
    }
}
