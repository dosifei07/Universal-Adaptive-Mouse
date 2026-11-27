using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class HistoryViewModelTests
{
    private static (HistoryViewModel Vm, FakeHistoryService Service) Create()
    {
        var service = new FakeHistoryService();
        service.Sessions.AddRange(
        [
            Samples.Session("old", "valorant", "gridshot", minutesAgo: 120, observations: 50),
            Samples.Session("new", "valorant", "tracking", minutesAgo: 5, observations: 70),
            Samples.Session("other", "cs2", "gridshot", minutesAgo: 30, observations: 30)
        ]);

        return (new HistoryViewModel(service), service);
    }

    [Fact]
    public async Task Load_ListsSessionsNewestFirstWithRealTotals()
    {
        var (vm, _) = Create();

        await vm.OnActivatedAsync();

        Assert.Equal(new[] { "new", "other", "old" }, vm.Sessions.Select(x => x.SessionId).ToList());
        Assert.Equal(new[] { "cs2", "valorant" }, vm.Games.ToList());
        Assert.Equal(150, vm.Sessions.Sum(x => x.ObservationCount));
        Assert.Equal(3, vm.Sessions.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task SessionRow_ShowsDurationFromRealTimestamps()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync();

        Assert.All(vm.Sessions, row => Assert.Equal(TimeSpan.FromMinutes(5), row.Duration));
        Assert.Equal("05:00", vm.Sessions[0].DurationText);
    }

    [Fact]
    public async Task GameFilter_NarrowsSessionsAndScenarios()
    {
        var (vm, service) = Create();
        await vm.RefreshAsync();

        await vm.SetFiltersAsync("valorant", null);

        Assert.Equal(2, vm.Sessions.Count);
        Assert.Equal(new[] { "gridshot", "tracking" }, vm.Scenarios.ToList());
        Assert.Equal(("valorant", (string?)null), service.SessionRequests.Last());
        Assert.True(vm.IsFiltered);
    }

    [Fact]
    public async Task ScenarioFilter_NarrowsFurther()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync();

        await vm.SetFiltersAsync("valorant", "tracking");

        Assert.Equal(new[] { "new" }, vm.Sessions.Select(x => x.SessionId).ToList());
    }

    [Fact]
    public async Task ChangingGameFromTheUi_ResetsTheScenario()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync();
        await vm.SetFiltersAsync("valorant", "tracking");

        vm.SelectedGame = "cs2";

        Assert.Equal("cs2", vm.SelectedGame);
        Assert.Null(vm.SelectedScenario);
        Assert.Equal(new[] { "other" }, vm.Sessions.Select(x => x.SessionId).ToList());
    }

    [Fact]
    public async Task ClearFilters_RestoresEverySession()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync();
        await vm.SetFiltersAsync("cs2", null);

        Assert.True(vm.ClearFiltersCommand.CanExecute(null));
        await vm.ClearFiltersCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Sessions.Count);
        Assert.False(vm.IsFiltered);
    }

    [Fact]
    public async Task AFilterThatNoLongerExists_IsDroppedInsteadOfHidingEverything()
    {
        var (vm, _) = Create();
        await vm.SetFiltersAsync("deleted-game", "gone");

        Assert.Null(vm.SelectedGame);
        Assert.Null(vm.SelectedScenario);
        Assert.Equal(3, vm.Sessions.Count);
    }

    [Fact]
    public async Task NoSessions_ReportsEmptyState()
    {
        var service = new FakeHistoryService();
        var vm = new HistoryViewModel(service);

        await vm.RefreshAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.IsFilteredEmpty);
        Assert.Equal("0", vm.SessionCountText);
    }

    [Fact]
    public async Task Failure_IsReported()
    {
        var (vm, service) = Create();
        service.Failure = new IOException("disk error");

        await vm.RefreshAsync();

        Assert.True(vm.HasError);
        Assert.Equal("disk error", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsEmpty);
    }
}
