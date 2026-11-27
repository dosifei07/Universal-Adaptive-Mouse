using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Base for the seven top-level pages. Adds identity (from the shared
/// navigation catalog), a "has loaded" flag for empty-state handling, and a
/// load helper in which only the most recent request may update the page.
/// </summary>
public abstract class PageViewModelBase : ViewModelBase
{
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private bool _hasLoaded;

    protected PageViewModelBase(PageKey key)
    {
        var item = NavigationCatalog.Get(key);

        Key = key;
        Title = item.Title;
        Description = item.Description;
    }

    public PageKey Key { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>
    /// True once a load has completed successfully at least once. Views use
    /// it to tell "not loaded yet" apart from "loaded and empty".
    /// </summary>
    public bool HasLoaded
    {
        get => _hasLoaded;
        private set => SetProperty(ref _hasLoaded, value);
    }

    /// <summary>
    /// True when leaving the page would discard user input. The main view
    /// model refuses to navigate away while this is set.
    /// </summary>
    public virtual bool HasUnsavedChanges => false;

    /// <summary>
    /// Loads data and applies it, unless a newer load has started in the
    /// meantime. A superseded request is cancelled, and neither its result
    /// nor its failure reaches the page. Returns true when the result was
    /// applied.
    /// </summary>
    protected async Task<bool> LoadLatestAsync<T>(
        Func<CancellationToken, Task<T>> load,
        Action<T> apply,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(apply);

        var version = Interlocked.Increment(ref _loadVersion);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();

        var applied = false;

        try
        {
            await RunSafeAsync(async () =>
            {
                T result;

                try
                {
                    result = await load(cts.Token);
                }
                catch when (version != Volatile.Read(ref _loadVersion))
                {
                    // Superseded: a newer load owns the page now.
                    return;
                }

                if (version != Volatile.Read(ref _loadVersion) ||
                    cts.IsCancellationRequested)
                {
                    return;
                }

                apply(result);
                applied = true;
            });
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, cts), cts))
            {
                cts.Dispose();
            }
        }

        if (applied)
        {
            HasLoaded = true;
        }

        return applied;
    }
}
