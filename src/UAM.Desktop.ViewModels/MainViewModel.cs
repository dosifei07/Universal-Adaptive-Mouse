using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Shell view model. Owns the seven pages, the sidebar, and routing. Pages
/// can ask for navigation (for example "review the pending recommendation")
/// through <see cref="ViewModelBase.NavigationRequested"/>; they never hold a
/// reference to each other.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly Dictionary<PageKey, PageViewModelBase> _pages;
    private CancellationTokenSource? _activationCts;
    private PageViewModelBase? _currentPage;

    public MainViewModel(IEnumerable<PageViewModelBase> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        _pages = new();

        foreach (var page in pages)
        {
            if (!_pages.TryAdd(page.Key, page))
            {
                throw new ArgumentException(
                    $"More than one page was supplied for '{page.Key}'.",
                    nameof(pages));
            }
        }

        var missing = NavigationCatalog.Items
            .Select(x => x.Key)
            .Where(key => !_pages.ContainsKey(key))
            .ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                "A page is required for every destination. Missing: " +
                string.Join(", ", missing) + ".",
                nameof(pages));
        }

        NavigationItems = NavigationCatalog.Items
            .Select(x => new NavigationItemViewModel(x))
            .ToList();

        foreach (var page in _pages.Values)
        {
            page.NavigationRequested += OnPageNavigationRequested;
        }

        NavigateCommand = new RelayCommand(
            parameter =>
            {
                var key = parameter switch
                {
                    PageKey pageKey => pageKey,
                    NavigationItemViewModel item => item.Key,
                    _ => (PageKey?)null
                };

                if (key is not null)
                {
                    // NavigateAsync reports its own failures, so the
                    // discarded task cannot fault unobserved.
                    _ = NavigateAsync(key.Value);
                }
            });
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public RelayCommand NavigateCommand { get; }

    public PageViewModelBase? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CurrentKey));
                OnPropertyChanged(nameof(CurrentTitle));
                OnPropertyChanged(nameof(CurrentDescription));
            }
        }
    }

    public PageKey? CurrentKey => _currentPage?.Key;

    public string CurrentTitle => _currentPage?.Title ?? string.Empty;

    public string CurrentDescription => _currentPage?.Description ?? string.Empty;

    public PageViewModelBase GetPage(PageKey key) => _pages[key];

    /// <summary>Opens the first page. Call once after the window is created.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return NavigateAsync(PageKey.Dashboard, cancellationToken);
    }

    /// <summary>
    /// Switches to a page and waits for it to finish loading. Returns false
    /// when navigation was refused because the current page has unsaved
    /// changes. Navigating to the current page does nothing; pages have their
    /// own refresh command.
    /// </summary>
    public async Task<bool> NavigateAsync(
        PageKey key,
        CancellationToken cancellationToken = default)
    {
        var target = _pages[key];

        if (ReferenceEquals(target, _currentPage))
        {
            return true;
        }

        if (_currentPage is { HasUnsavedChanges: true } current)
        {
            ErrorMessage =
                $"Save or discard your changes on {current.Title} before " +
                "leaving the page.";
            return false;
        }

        ErrorMessage = null;

        // A page that is still loading from the previous navigation no longer
        // matters once the user has moved on.
        _activationCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activationCts = cts;

        CurrentPage = target;

        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Key == key;
        }

        try
        {
            await target.OnActivatedAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer navigation.
        }
        catch (Exception exception)
        {
            // Pages report their own load failures; this guards against a
            // page that throws before it can.
            if (ReferenceEquals(CurrentPage, target))
            {
                ErrorMessage = exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_activationCts, cts))
            {
                _activationCts = null;
            }
        }

        return true;
    }

    private void OnPageNavigationRequested(object? sender, PageKey key)
    {
        // Only the visible page may steer navigation.
        if (!ReferenceEquals(sender, _currentPage))
        {
            return;
        }

        _ = NavigateAsync(key);
    }
}
