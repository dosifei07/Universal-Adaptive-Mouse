using UAM.Application;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class IntegrationsViewModelTests
{
    [Fact]
    public void OnlyRealCapabilitiesAreMarkedAvailable()
    {
        var vm = new IntegrationsViewModel();

        var available = vm.Providers.Where(x => x.IsAvailable).ToList();

        Assert.Single(available);
        Assert.Equal("Training JSON import", available[0].Name);
    }

    [Fact]
    public void Aimlabs_IsResearchOnlyAndNeverClaimsAConnection()
    {
        var vm = new IntegrationsViewModel();

        var aimlabs = vm.Providers.Single(x => x.Name == "Aimlabs");

        Assert.Equal(IntegrationStatus.Research, aimlabs.Status);
        Assert.False(aimlabs.IsAvailable);
        Assert.Contains("not connected", aimlabs.StatusText);
    }

    [Fact]
    public async Task Activation_NeedsNoDataAndNeverBusy()
    {
        var vm = new IntegrationsViewModel();

        await vm.OnActivatedAsync();

        Assert.False(vm.IsBusy);
        Assert.False(vm.HasError);
    }
}

public sealed class SettingsViewModelTests
{
    private static readonly UamApplicationOptions Options = new()
    {
        DatabasePath = "data/uam.db",
        PlayerId = "player-xyz",
        ApprovedBy = "reviewer"
    };

    [Fact]
    public void ShowsTheConfiguredIdentityAndAnAbsoluteDatabasePath()
    {
        var vm = new SettingsViewModel(Options, new FakeDashboardService());

        Assert.Equal("player-xyz", vm.PlayerId);
        Assert.Equal("reviewer", vm.ApprovedBy);
        Assert.True(Path.IsPathRooted(vm.DatabasePath));
        Assert.EndsWith("uam.db", vm.DatabasePath);
    }

    [Fact]
    public async Task ReachableDatabase_ReportsConnectedWithRealCounts()
    {
        var service = new FakeDashboardService
        {
            Handler = _ => Task.FromResult(Samples.Dashboard())
        };
        var vm = new SettingsViewModel(Options, service);

        await vm.OnActivatedAsync();

        Assert.True(vm.IsDatabaseReachable);
        Assert.Equal("Connected", vm.DatabaseStatus);
        Assert.Equal("3", vm.SessionCountText);
        Assert.Equal("1200", new string(vm.ObservationCountText.Where(char.IsDigit).ToArray()));
    }

    [Fact]
    public async Task UnreachableDatabase_ReportsUnavailableAndShowsNoCounts()
    {
        var service = new FakeDashboardService
        {
            Handler = _ => Task.FromException<DashboardSummary>(
                new IOException("cannot open database"))
        };
        var vm = new SettingsViewModel(Options, service);

        await vm.OnActivatedAsync();

        Assert.False(vm.IsDatabaseReachable);
        Assert.Equal("Unavailable", vm.DatabaseStatus);
        Assert.Equal("cannot open database", vm.ErrorMessage);
        Assert.Equal("—", vm.SessionCountText);
    }

    [Fact]
    public async Task AFailedRecheck_DoesNotKeepStaleCounts()
    {
        var service = new FakeDashboardService
        {
            Handler = _ => Task.FromResult(Samples.Dashboard())
        };
        var vm = new SettingsViewModel(Options, service);
        await vm.RefreshAsync();

        service.Handler = _ => Task.FromException<DashboardSummary>(new IOException("gone"));
        await vm.RefreshAsync();

        Assert.False(vm.IsDatabaseReachable);
        Assert.Equal("—", vm.ObservationCountText);
    }
}
