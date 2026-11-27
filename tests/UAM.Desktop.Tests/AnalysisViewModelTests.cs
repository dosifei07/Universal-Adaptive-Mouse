using UAM.Application;
using UAM.Core.Models;
using UAM.Core.Validation;
using UAM.Desktop.Shared.Navigation;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class AnalysisViewModelTests
{
    private static (AnalysisViewModel Vm, FakeAnalysisService Service) Create()
    {
        var service = new FakeAnalysisService();
        return (new AnalysisViewModel(service), service);
    }

    [Fact]
    public async Task Load_PopulatesGlobalAndGameHeatmapsFromTheService()
    {
        var (vm, _) = Create();

        await vm.OnActivatedAsync();

        Assert.True(vm.HasData);
        Assert.Equal(8, vm.GlobalHeatmap.Count);
        Assert.Equal(8, vm.GameHeatmap.Count);
        Assert.True(vm.GlobalHeatmap[0].HasData);
        Assert.False(vm.GlobalHeatmap[5].HasData);
        Assert.Equal(3, vm.GlobalSpeeds.Count);
        Assert.True(vm.GameSpeeds.Single(x => x.Band == SpeedBand.Fast).HasData);
    }

    [Fact]
    public async Task Load_ExposesGamesAndSelectsTheAnalyzedOne()
    {
        var (vm, _) = Create();

        await vm.RefreshAsync();

        Assert.Equal(new[] { "valorant", "cs2" }, vm.Games.ToList());
        Assert.Equal("valorant", vm.SelectedGame);
    }

    [Fact]
    public async Task SelectGameAsync_AnalyzesThatGame()
    {
        var (vm, service) = Create();
        await vm.RefreshAsync();

        service.Handler = (game, _) => Task.FromResult(
            Samples.Analysis(gameId: game));

        await vm.SelectGameAsync("cs2");

        Assert.Equal("cs2", service.RequestedGames.Last());
        Assert.Equal("cs2", vm.SelectedGame);
    }

    [Fact]
    public async Task ChangingSelectedGameFromTheUi_ReloadsWithoutLooping()
    {
        var (vm, service) = Create();
        await vm.RefreshAsync();
        service.Handler = (game, _) => Task.FromResult(Samples.Analysis(gameId: game));
        var before = service.RequestedGames.Count;

        vm.SelectedGame = "cs2";

        Assert.Equal(before + 1, service.RequestedGames.Count);
        Assert.Equal("cs2", service.RequestedGames.Last());
    }

    [Fact]
    public async Task EmptyDatabase_ShowsEmptyStateAndDisablesRecommendation()
    {
        var (vm, service) = Create();
        service.Handler = (_, _) => Task.FromResult(
            Samples.Analysis(gameId: null, hasData: false));

        await vm.RefreshAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasData);
        Assert.False(vm.HasGameData);
        Assert.False(vm.GenerateRecommendationCommand.CanExecute(null));
        Assert.All(vm.GlobalHeatmap, cell => Assert.False(cell.HasData));
    }

    [Fact]
    public async Task Rejections_AreSummarizedPerValidationCode()
    {
        var (vm, service) = Create();
        service.Handler = (_, _) => Task.FromResult(Samples.Analysis(
            rejections:
            [
                Samples.Rejected(ObservationValidationCode.SpeedOutlier),
                Samples.Rejected(
                    ObservationValidationCode.SpeedOutlier,
                    ObservationValidationCode.TimestampInFuture),
                Samples.Rejected(ObservationValidationCode.SpeedOutlier)
            ]));

        await vm.RefreshAsync();

        Assert.True(vm.HasRejections);
        Assert.Equal(2, vm.RejectionSummaries.Count);
        Assert.StartsWith("SpeedOutlier", vm.RejectionSummaries[0]);
        Assert.StartsWith("TimestampInFuture", vm.RejectionSummaries[1]);
    }

    [Fact]
    public async Task GenerateRecommendation_OnlyNavigatesToOptimization()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync();
        PageKey? requested = null;
        vm.NavigationRequested += (_, key) => requested = key;

        vm.GenerateRecommendationCommand.Execute(null);

        Assert.Equal(PageKey.Optimization, requested);
    }

    [Fact]
    public async Task Failure_IsReportedWithoutStaleData()
    {
        var (vm, service) = Create();
        service.Handler = (_, _) => Task.FromException<AnalysisResult>(
            new InvalidOperationException("analysis failed"));

        await vm.RefreshAsync();

        Assert.True(vm.HasError);
        Assert.Equal("analysis failed", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasLoaded);
        Assert.Equal("—", vm.TotalObservationsText);
    }
}
