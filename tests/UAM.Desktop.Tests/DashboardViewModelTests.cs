using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Navigation;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class DashboardViewModelTests
{
    private static (DashboardViewModel Vm, FakeDashboardService Service) Create()
    {
        var service = new FakeDashboardService();
        return (new DashboardViewModel(service), service);
    }

    [Fact]
    public void BeforeLoading_ShowsNoNumbersAtAll()
    {
        var (vm, service) = Create();

        Assert.Equal("—", vm.SessionCountText);
        Assert.Equal("—", vm.ObservationCountText);
        Assert.False(vm.HasLoaded);
        Assert.False(vm.IsEmpty);
        Assert.Equal(0, service.Calls);
        Assert.All(vm.Heatmap, cell => Assert.False(cell.HasData));
    }

    [Fact]
    public async Task EmptyDatabase_ReportsAnEmptyStateWithoutInventedMetrics()
    {
        var (vm, _) = Create();

        await vm.OnActivatedAsync();

        Assert.True(vm.HasLoaded);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasData);
        Assert.False(vm.HasError);
        Assert.False(vm.HasPendingRecommendation);
        Assert.False(vm.HasActiveProfile);
        Assert.All(vm.Heatmap, cell => Assert.Equal(HeatmapTone.NoData, cell.Tone));
        Assert.All(vm.SpeedBands, row => Assert.False(row.HasData));
    }

    [Fact]
    public async Task WithData_ExposesTheServiceValuesOnly()
    {
        var (vm, service) = Create();
        var pending = Samples.Stored("rec_1");
        service.Handler = _ => Task.FromResult(Samples.Dashboard(pending: pending));

        await vm.RefreshAsync();

        Assert.True(vm.HasData);
        Assert.False(vm.IsEmpty);
        Assert.Equal("3", vm.SessionCountText);
        Assert.Equal("2", vm.GameCountText);
        Assert.Equal("1", vm.PendingApprovalCountText);

        Assert.True(vm.Heatmap[0].HasData);
        Assert.Equal(0.10, vm.Heatmap[0].MeanErrorRatio);
        Assert.False(vm.Heatmap[1].HasData);

        Assert.True(vm.SpeedBands.Single(x => x.Band == SpeedBand.Medium).HasData);
        Assert.False(vm.SpeedBands.Single(x => x.Band == SpeedBand.Slow).HasData);
    }

    [Fact]
    public async Task PendingRecommendation_IsShownButNeverApproved()
    {
        var (vm, service) = Create();
        service.Handler = _ => Task.FromResult(
            Samples.Dashboard(pending: Samples.Stored("rec_1")));

        await vm.RefreshAsync();

        Assert.True(vm.HasPendingRecommendation);
        Assert.Equal("rec_1", vm.PendingRecommendation!.Id);
        Assert.True(vm.PendingRecommendation.IsPending);
    }

    [Fact]
    public async Task ReviewRecommendation_NavigatesToOptimization()
    {
        var (vm, service) = Create();
        service.Handler = _ => Task.FromResult(
            Samples.Dashboard(pending: Samples.Stored("rec_1")));
        await vm.RefreshAsync();

        PageKey? requested = null;
        vm.NavigationRequested += (_, key) => requested = key;

        Assert.True(vm.ReviewRecommendationCommand.CanExecute(null));
        vm.ReviewRecommendationCommand.Execute(null);

        Assert.Equal(PageKey.Optimization, requested);
    }

    [Fact]
    public async Task ReviewRecommendation_IsUnavailableWithoutAPendingRecommendation()
    {
        var (vm, _) = Create();
        await vm.RefreshAsync();

        Assert.False(vm.ReviewRecommendationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ActiveProfile_IsDescribedFromRealData()
    {
        var (vm, service) = Create();
        service.Handler = _ => Task.FromResult(
            Samples.Dashboard(active: Samples.Approved("prf_1", "cs2")));

        await vm.RefreshAsync();

        Assert.True(vm.HasActiveProfile);
        Assert.Contains("cs2", vm.ActiveProfileText);
    }

    [Fact]
    public async Task WhileLoading_IsBusyIsSetAndCleared()
    {
        var (vm, service) = Create();
        var gate = new TaskCompletionSource<DashboardSummary>();
        service.Handler = _ => gate.Task;

        var load = vm.RefreshAsync();

        Assert.True(vm.IsBusy);
        Assert.False(vm.HasLoaded);

        gate.SetResult(Samples.Dashboard());
        await load;

        Assert.False(vm.IsBusy);
        Assert.True(vm.HasLoaded);
    }

    [Fact]
    public async Task RefreshCommand_IsDisabledWhileLoading()
    {
        var (vm, service) = Create();
        var gate = new TaskCompletionSource<DashboardSummary>();
        service.Handler = _ => gate.Task;

        var load = vm.RefreshAsync();
        Assert.False(vm.RefreshCommand.CanExecute(null));

        gate.SetResult(Samples.Dashboard());
        await load;

        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadFailure_IsReportedAndLeavesNoFakeData()
    {
        var (vm, service) = Create();
        service.Handler = _ => Task.FromException<DashboardSummary>(
            new IOException("database is locked"));

        await vm.RefreshAsync();

        Assert.True(vm.HasError);
        Assert.Equal("database is locked", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasLoaded);
        Assert.False(vm.IsEmpty);
        Assert.Equal("—", vm.SessionCountText);
    }

    [Fact]
    public async Task RetryAfterFailure_ClearsTheErrorAndShowsData()
    {
        var (vm, service) = Create();
        service.Handler = _ => Task.FromException<DashboardSummary>(new IOException("boom"));
        await vm.RefreshAsync();

        service.Handler = _ => Task.FromResult(Samples.Dashboard());
        await vm.RefreshAsync();

        Assert.False(vm.HasError);
        Assert.True(vm.HasData);
    }

    [Fact]
    public async Task OlderSlowRequest_CannotOverwriteANewerResult()
    {
        var (vm, service) = Create();
        var slow = new TaskCompletionSource<DashboardSummary>();
        var calls = 0;

        service.Handler = _ => ++calls == 1
            ? slow.Task
            : Task.FromResult(Samples.Dashboard(pending: Samples.Stored("rec_new")));

        var first = vm.RefreshAsync();
        await vm.RefreshAsync();

        Assert.True(vm.HasPendingRecommendation);

        slow.SetResult(Samples.EmptyDashboard());
        await first;

        Assert.True(vm.HasPendingRecommendation);
        Assert.True(vm.HasData);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task OlderRequestThatFailsLate_DoesNotShowAnError()
    {
        var (vm, service) = Create();
        var slow = new TaskCompletionSource<DashboardSummary>();
        var calls = 0;

        service.Handler = _ => ++calls == 1
            ? slow.Task
            : Task.FromResult(Samples.Dashboard());

        var first = vm.RefreshAsync();
        await vm.RefreshAsync();

        slow.SetException(new IOException("stale failure"));
        await first;

        Assert.False(vm.HasError);
        Assert.True(vm.HasData);
    }

    [Fact]
    public async Task Cancellation_IsNotAnError()
    {
        var (vm, service) = Create();
        service.Handler = token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Samples.Dashboard());
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await vm.RefreshAsync(cts.Token);

        Assert.False(vm.HasError);
        Assert.False(vm.HasLoaded);
        Assert.False(vm.IsBusy);
    }
}
