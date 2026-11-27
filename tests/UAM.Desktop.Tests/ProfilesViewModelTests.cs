using UAM.Core.Models;
using UAM.Desktop.Shared.Navigation;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

public sealed class ProfilesViewModelTests
{
    private static (ProfilesViewModel Vm, FakeProfileService Service) Create(
        params ApprovedProfile[] profiles)
    {
        var service = new FakeProfileService();
        service.Items.AddRange(profiles);

        return (new ProfilesViewModel(service, Path.Combine(Path.GetTempPath(), "uam-export-tests")), service);
    }

    [Fact]
    public async Task Load_ShowsOnlyTheLatestApprovedProfilePerGame()
    {
        var (vm, _) = Create(
            Samples.Approved("old", "valorant", minutesAgo: 60),
            Samples.Approved("new", "valorant", minutesAgo: 5, edited: true),
            Samples.Approved("cs", "cs2", minutesAgo: 30));

        await vm.OnActivatedAsync();

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal(new[] { "valorant", "cs2" }, vm.Profiles.Select(x => x.GameId).ToList());
        Assert.True(vm.Profiles[0].IsManuallyEdited);
        Assert.Equal("new", vm.Profiles[0].Approved.Id);
    }

    [Fact]
    public async Task NoApprovedProfiles_ShowsEmptyState()
    {
        var (vm, _) = Create();

        await vm.RefreshAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasSelection);
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadFailure_IsReported()
    {
        var (vm, service) = Create();
        service.ListHandler = _ => Task.FromException<IReadOnlyList<ApprovedProfile>>(
            new IOException("db unreadable"));

        await vm.RefreshAsync();

        Assert.Equal("db unreadable", vm.ErrorMessage);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SelectingAProfile_SuggestsAnExportFileNameFromTheProfile()
    {
        var (vm, _) = Create(Samples.Approved("p1", "valorant"));
        await vm.RefreshAsync();

        vm.SelectedProfile = vm.Profiles[0];

        Assert.EndsWith(".json", vm.ExportPath);
        Assert.Contains("uam-profile-valorant-", vm.ExportPath);
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task Export_UsesTheServiceAndReportsTheRealPath()
    {
        var (vm, service) = Create(Samples.Approved("p1", "valorant"));
        await vm.RefreshAsync();
        vm.SelectedProfile = vm.Profiles[0];
        vm.ExportPath = Path.Combine(Path.GetTempPath(), "x", "profile.json");

        await vm.ExportAsync();

        Assert.Single(service.ExportCalls);
        Assert.Equal("valorant", service.ExportCalls[0].GameId);
        Assert.Equal(vm.ExportPath, service.ExportCalls[0].Path);
        Assert.Contains("profile.json", vm.StatusMessage);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Export_Failure_IsShownWithoutASuccessMessage()
    {
        var (vm, service) = Create(Samples.Approved("p1"));
        service.ExportFailure = new UnauthorizedAccessException("denied");
        await vm.RefreshAsync();
        vm.SelectedProfile = vm.Profiles[0];

        await vm.ExportAsync();

        Assert.Equal("denied", vm.ErrorMessage);
        Assert.False(vm.HasStatusMessage);
    }

    [Fact]
    public async Task Export_IsUnavailableWithoutAPath()
    {
        var (vm, service) = Create(Samples.Approved("p1"));
        await vm.RefreshAsync();
        vm.SelectedProfile = vm.Profiles[0];
        vm.ExportPath = "  ";

        await vm.ExportAsync();

        Assert.False(vm.CanExport);
        Assert.Empty(service.ExportCalls);
    }

    // ---- Editing -----------------------------------------------------

    private static async Task<(ProfilesViewModel Vm, FakeProfileService Service)> Editing()
    {
        var (vm, service) = Create(Samples.Approved("p1", "valorant"));
        await vm.RefreshAsync();
        vm.SelectedProfile = vm.Profiles[0];
        vm.BeginEdit();
        return (vm, service);
    }

    [Fact]
    public async Task BeginEdit_LoadsEditorsFromTheProfile_WithNoChanges()
    {
        var (vm, _) = await Editing();

        Assert.True(vm.IsEditing);
        Assert.Equal(8, vm.EditDirectionalGains.Count);
        Assert.Equal(3, vm.EditSpeedGains.Count);
        Assert.Equal(0.98, vm.EditDirectionalGains[0].Gain);
        Assert.Equal(800, vm.EditDpi);
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.SaveEditCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangingAGain_MarksUnsavedChangesAndEnablesSave()
    {
        var (vm, _) = await Editing();

        vm.EditDirectionalGains[0].Gain = 0.97;

        Assert.True(vm.HasUnsavedChanges);
        Assert.True(vm.SaveEditCommand.CanExecute(null));
        Assert.True(vm.EditDirectionalGains[0].HasChanged);
    }

    [Fact]
    public async Task Save_SendsEditedGainsToTheServiceAndLeavesOthersUntouched()
    {
        var (vm, service) = await Editing();
        vm.EditDirectionalGains[2].Gain = 1.01;
        vm.EditSpeedGains[0].Gain = 0.995;
        vm.EditDpi = 1600;

        await vm.SaveEditAsync();

        var (gameId, profile) = Assert.Single(service.SaveCalls);
        Assert.Equal("valorant", gameId);
        Assert.Equal(1.01, profile.DirectionalGains[2]);
        Assert.Equal(0.98, profile.DirectionalGains[0]);
        Assert.Equal(0.995, profile.SpeedGains[SpeedBand.Slow]);
        Assert.Equal(0.99, profile.SpeedGains[SpeedBand.Medium]);
        Assert.Equal(1600, profile.Dpi);
        Assert.Equal(0.4, profile.InGameSensitivity);

        Assert.False(vm.IsEditing);
        Assert.False(vm.HasUnsavedChanges);
        Assert.True(vm.HasStatusMessage);
        Assert.Equal("valorant", vm.SelectedProfile?.GameId);
        Assert.True(vm.SelectedProfile!.IsManuallyEdited);
        Assert.Single(vm.Profiles);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Save_RejectsGainsThatAreNotPositiveNumbers(double invalid)
    {
        var (vm, service) = await Editing();
        vm.EditDirectionalGains[0].Gain = invalid;

        await vm.SaveEditAsync();

        Assert.Empty(service.SaveCalls);
        Assert.True(vm.HasError);
        Assert.True(vm.IsEditing);
        Assert.False(vm.EditorIsValid);
    }

    [Fact]
    public async Task Save_Failure_KeepsTheEditorOpenSoNothingIsLost()
    {
        var (vm, service) = await Editing();
        service.SaveFailure = new InvalidOperationException("No approved profile");
        vm.EditDirectionalGains[0].Gain = 0.97;

        await vm.SaveEditAsync();

        Assert.Equal("No approved profile", vm.ErrorMessage);
        Assert.True(vm.IsEditing);
        Assert.Equal(0.97, vm.EditDirectionalGains[0].Gain);
        Assert.False(vm.HasStatusMessage);
    }

    [Fact]
    public async Task DiscardEdit_ClosesTheEditorWithoutSaving()
    {
        var (vm, service) = await Editing();
        vm.EditDirectionalGains[0].Gain = 0.5;

        vm.DiscardEdit();

        Assert.False(vm.IsEditing);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Empty(service.SaveCalls);
        Assert.Empty(vm.EditDirectionalGains);
    }

    [Fact]
    public async Task WhileEditing_SelectionCannotChangeAndExportAndRefreshAreBlocked()
    {
        var (vm, service) = Create(
            Samples.Approved("p1", "valorant"),
            Samples.Approved("p2", "cs2", minutesAgo: 10));
        await vm.RefreshAsync();
        vm.SelectedProfile = vm.Profiles[0];
        vm.BeginEdit();
        var editing = vm.SelectedProfile;

        vm.SelectedProfile = vm.Profiles[1];
        await vm.ExportAsync();
        await vm.RefreshAsync();

        Assert.Same(editing, vm.SelectedProfile);
        Assert.Empty(service.ExportCalls);
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public async Task OnlyAnUnsavedEdit_BlocksLeavingThePage()
    {
        var (vm, _) = await Editing();
        Assert.False(vm.HasUnsavedChanges);

        vm.EditDirectionalGains[0].Gain = 0.9;
        Assert.True(vm.HasUnsavedChanges);

        vm.DiscardEdit();
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task ProfilesPage_IsBlockedFromLeavingByTheShell()
    {
        var (vm, _) = await Editing();
        vm.EditDirectionalGains[0].Gain = 0.9;

        var pages = StubPage.All().Where(x => x.Key != PageKey.Profiles).Cast<PageViewModelBase>().ToList();
        pages.Add(vm);
        var main = new MainViewModel(pages);
        await main.NavigateAsync(PageKey.Profiles);

        var allowed = await main.NavigateAsync(PageKey.Dashboard);

        Assert.False(allowed);
        Assert.Equal(PageKey.Profiles, main.CurrentKey);
    }
}
