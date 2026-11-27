using UAM.Desktop.Shared.Navigation;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class MainViewModelTests
{
    private static (MainViewModel Main, List<StubPage> Pages) Create()
    {
        var pages = StubPage.All();
        return (new MainViewModel(pages), pages);
    }

    private static StubPage Page(List<StubPage> pages, PageKey key) =>
        pages.Single(x => x.Key == key);

    [Fact]
    public void Constructor_RequiresEverySection()
    {
        var pages = StubPage.All().Where(x => x.Key != PageKey.Settings).ToList();

        var exception = Assert.Throws<ArgumentException>(() => new MainViewModel(pages));

        Assert.Contains("Settings", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsDuplicatePages()
    {
        var pages = StubPage.All();
        pages.Add(new StubPage(PageKey.Dashboard));

        Assert.Throws<ArgumentException>(() => new MainViewModel(pages));
    }

    [Fact]
    public void NavigationItems_FollowTheCatalogOrder()
    {
        var (main, _) = Create();

        Assert.Equal(
            NavigationCatalog.Items.Select(x => x.Key).ToList(),
            main.NavigationItems.Select(x => x.Key).ToList());
    }

    [Fact]
    public void BeforeInitialization_NoPageIsCurrent()
    {
        var (main, _) = Create();

        Assert.Null(main.CurrentPage);
        Assert.Null(main.CurrentKey);
        Assert.Equal(string.Empty, main.CurrentTitle);
        Assert.All(main.NavigationItems, x => Assert.False(x.IsSelected));
    }

    [Fact]
    public async Task InitializeAsync_OpensAndActivatesTheDashboard()
    {
        var (main, pages) = Create();

        await main.InitializeAsync();

        Assert.Equal(PageKey.Dashboard, main.CurrentKey);
        Assert.Same(Page(pages, PageKey.Dashboard), main.CurrentPage);
        Assert.Equal(1, Page(pages, PageKey.Dashboard).Activations);
        Assert.Equal("Dashboard", main.CurrentTitle);
    }

    [Fact]
    public async Task NavigateAsync_SwitchesPageActivatesItAndMovesTheSelection()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();

        var result = await main.NavigateAsync(PageKey.History);

        Assert.True(result);
        Assert.Equal(PageKey.History, main.CurrentKey);
        Assert.Equal(1, Page(pages, PageKey.History).Activations);
        Assert.Single(main.NavigationItems, x => x.IsSelected);
        Assert.True(main.NavigationItems.Single(x => x.Key == PageKey.History).IsSelected);
    }

    [Fact]
    public async Task NavigateAsync_ToTheCurrentPageDoesNotReload()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();

        await main.NavigateAsync(PageKey.Dashboard);

        Assert.Equal(1, Page(pages, PageKey.Dashboard).Activations);
    }

    [Fact]
    public async Task ReturningToAPage_ActivatesItAgainSoDataIsFresh()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();
        await main.NavigateAsync(PageKey.Analysis);
        await main.NavigateAsync(PageKey.Dashboard);

        Assert.Equal(2, Page(pages, PageKey.Dashboard).Activations);
    }

    [Fact]
    public async Task PageNavigationRequest_FromTheCurrentPage_Navigates()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();

        Page(pages, PageKey.Dashboard).Request(PageKey.Optimization);

        Assert.Equal(PageKey.Optimization, main.CurrentKey);
        Assert.Equal(1, Page(pages, PageKey.Optimization).Activations);
    }

    [Fact]
    public async Task PageNavigationRequest_FromAHiddenPage_IsIgnored()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();

        Page(pages, PageKey.Settings).Request(PageKey.Profiles);

        Assert.Equal(PageKey.Dashboard, main.CurrentKey);
    }

    [Fact]
    public async Task UnsavedChanges_BlockNavigationWithAMessage()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();
        await main.NavigateAsync(PageKey.Profiles);
        Page(pages, PageKey.Profiles).Unsaved = true;

        var result = await main.NavigateAsync(PageKey.Dashboard);

        Assert.False(result);
        Assert.Equal(PageKey.Profiles, main.CurrentKey);
        Assert.True(main.HasError);
        Assert.Contains("Game Profiles", main.ErrorMessage);
    }

    [Fact]
    public async Task NavigationIsAllowedOnceChangesAreResolved_AndClearsTheMessage()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();
        await main.NavigateAsync(PageKey.Profiles);
        var profiles = Page(pages, PageKey.Profiles);

        profiles.Unsaved = true;
        await main.NavigateAsync(PageKey.Dashboard);
        profiles.Unsaved = false;

        var result = await main.NavigateAsync(PageKey.Dashboard);

        Assert.True(result);
        Assert.Equal(PageKey.Dashboard, main.CurrentKey);
        Assert.False(main.HasError);
    }

    [Fact]
    public async Task ActivationFailure_IsReportedInsteadOfEscaping()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();
        Page(pages, PageKey.Analysis).ActivationHandler =
            _ => throw new InvalidOperationException("page exploded");

        var result = await main.NavigateAsync(PageKey.Analysis);

        Assert.True(result);
        Assert.Equal(PageKey.Analysis, main.CurrentKey);
        Assert.Equal("page exploded", main.ErrorMessage);
    }

    [Fact]
    public async Task NewNavigation_CancelsTheActivationOfThePreviousPage()
    {
        var (main, pages) = Create();
        await main.InitializeAsync();

        var gate = new TaskCompletionSource();
        var slow = Page(pages, PageKey.Analysis);
        slow.ActivationHandler = async token =>
        {
            await gate.Task;
            token.ThrowIfCancellationRequested();
        };

        var firstNavigation = main.NavigateAsync(PageKey.Analysis);
        var firstToken = slow.LastToken;

        await main.NavigateAsync(PageKey.History);

        Assert.True(firstToken.IsCancellationRequested);

        gate.SetResult();
        await firstNavigation;

        Assert.Equal(PageKey.History, main.CurrentKey);
        Assert.False(main.HasError);
    }

    [Fact]
    public async Task NavigateCommand_AcceptsAPageKeyOrASidebarItem()
    {
        var (main, _) = Create();
        await main.InitializeAsync();

        main.NavigateCommand.Execute(PageKey.Settings);
        Assert.Equal(PageKey.Settings, main.CurrentKey);

        var item = main.NavigationItems.Single(x => x.Key == PageKey.Integrations);
        main.NavigateCommand.Execute(item);
        Assert.Equal(PageKey.Integrations, main.CurrentKey);
    }

    [Fact]
    public async Task NavigateCommand_IgnoresUnknownParameters()
    {
        var (main, _) = Create();
        await main.InitializeAsync();

        main.NavigateCommand.Execute("not a page");
        main.NavigateCommand.Execute(null);

        Assert.Equal(PageKey.Dashboard, main.CurrentKey);
    }
}
