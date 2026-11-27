using UAM.Core.Models;
using UAM.Desktop.Shared.Navigation;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

/// <summary>
/// The approval workflow. These tests exist to prove that nothing is approved
/// unless the user explicitly begins and confirms approval of one actionable,
/// pending recommendation, and that the only route is the service interface.
/// </summary>
public sealed class OptimizationViewModelTests
{
    private static (OptimizationViewModel Vm, FakeOptimizationService Service, FakeAnalysisService Analysis)
        Create(params StoredRecommendation[] items)
    {
        var service = new FakeOptimizationService();
        service.Items.AddRange(items);

        var analysis = new FakeAnalysisService();
        return (new OptimizationViewModel(service, analysis), service, analysis);
    }

    private static RecommendationItemViewModel Item(OptimizationViewModel vm, string id) =>
        vm.Recommendations.Single(x => x.Id == id);

    // ---- Loading -----------------------------------------------------

    [Fact]
    public async Task Load_ListsRecommendationsAndGames_WithoutApprovingAnything()
    {
        var (vm, service, _) = Create(
            Samples.Stored("rec_1"),
            Samples.Stored("rec_2", RecommendationStatus.Rejected));

        await vm.OnActivatedAsync();

        Assert.Equal(2, vm.Recommendations.Count);
        Assert.Equal(new[] { "valorant", "cs2" }, vm.Games.ToList());
        Assert.Equal("valorant", vm.SelectedGame);
        Assert.Empty(service.ApproveCalls);
        Assert.Empty(service.RejectCalls);
    }

    [Fact]
    public async Task Selecting_NeverApproves()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();

        vm.SelectedRecommendation = Item(vm, "rec_1");

        Assert.True(vm.HasSelection);
        Assert.False(vm.IsConfirmingApproval);
        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task ProposedGains_AreShownForTheSelectedRecommendation()
    {
        var (vm, _, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();

        var item = Item(vm, "rec_1");

        Assert.Equal(8, item.DirectionalGains.Count);
        Assert.Equal(3, item.SpeedGains.Count);
        Assert.Equal(0.98, item.DirectionalGains[0].Gain);
        Assert.False(item.DirectionalGains[0].IsNeutral);
        Assert.True(item.DirectionalGains[1].IsNeutral);
    }

    [Fact]
    public async Task NoTrainingData_ShowsGuidanceAndDisablesGeneration()
    {
        var (vm, _, analysis) = Create();
        analysis.GamesHandler = _ => Task.FromResult<IReadOnlyList<string>>([]);

        await vm.RefreshAsync();

        Assert.True(vm.NeedsTrainingData);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.CanGenerate);
        Assert.False(vm.GenerateCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadFailure_IsReported()
    {
        var (vm, service, _) = Create();
        service.ListHandler = _ => Task.FromException<IReadOnlyList<StoredRecommendation>>(
            new IOException("read failed"));

        await vm.RefreshAsync();

        Assert.Equal("read failed", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task StatusFilter_ShowsOnlyMatchingRecommendations()
    {
        var (vm, _, _) = Create(
            Samples.Stored("a"),
            Samples.Stored("b", RecommendationStatus.Rejected),
            Samples.Stored("c", RecommendationStatus.Approved));
        await vm.RefreshAsync();

        vm.StatusFilter = RecommendationStatus.Rejected;

        Assert.Equal(new[] { "b" }, vm.Recommendations.Select(x => x.Id).ToList());

        vm.StatusFilter = null;

        Assert.Equal(3, vm.Recommendations.Count);
    }

    // ---- Generating --------------------------------------------------

    [Fact]
    public async Task Generate_CreatesAPendingRecommendation_ButDoesNotApproveIt()
    {
        var (vm, service, _) = Create();
        await vm.RefreshAsync();

        await vm.GenerateAsync();

        Assert.Equal(new[] { "valorant" }, service.GenerateCalls);
        Assert.Empty(service.ApproveCalls);
        Assert.Equal("rec_new", vm.SelectedRecommendation?.Id);
        Assert.Equal(RecommendationStatus.Pending, vm.SelectedRecommendation?.Status);
        Assert.True(vm.HasStatusMessage);
        Assert.False(vm.IsConfirmingApproval);
    }

    [Fact]
    public async Task Generate_InformationalResult_CannotBeApproved()
    {
        var (vm, service, _) = Create();
        service.GenerateResult = () => Samples.Stored(
            "rec_info", RecommendationStatus.Informational, actionable: false);
        await vm.RefreshAsync();

        await vm.GenerateAsync();

        Assert.False(vm.SelectedRecommendation!.CanApprove);
        Assert.False(vm.CanBeginApproval);
        Assert.Contains("cannot be approved", vm.StatusMessage);
        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task Generate_Failure_IsShown()
    {
        var (vm, service, _) = Create();
        service.GenerateFailure = new InvalidOperationException("No training data");
        await vm.RefreshAsync();

        await vm.GenerateAsync();

        Assert.Equal("No training data", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasStatusMessage);
    }

    [Fact]
    public async Task Generate_UsesTheSelectedGame()
    {
        var (vm, service, _) = Create();
        await vm.RefreshAsync();
        vm.SelectedGame = "cs2";

        await vm.GenerateAsync();

        Assert.Equal(new[] { "cs2" }, service.GenerateCalls);
    }

    // ---- Approval boundary -------------------------------------------

    [Fact]
    public async Task Approval_RequiresBeginThenConfirm_AndCallsTheServiceOnce()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");

        Assert.True(vm.CanBeginApproval);
        Assert.False(vm.CanConfirmApproval);

        vm.BeginApproval();

        Assert.True(vm.IsConfirmingApproval);
        Assert.Empty(service.ApproveCalls);
        Assert.NotEqual(string.Empty, vm.ApprovalPrompt);

        await vm.ConfirmApprovalAsync();

        Assert.Equal(new[] { "rec_1" }, service.ApproveCalls);
        Assert.False(vm.IsConfirmingApproval);
        Assert.Equal(RecommendationStatus.Approved, vm.SelectedRecommendation?.Status);
        Assert.Contains("nothing was applied", vm.StatusMessage);
    }

    [Fact]
    public async Task ConfirmWithoutBegin_DoesNotApprove()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");

        await vm.ConfirmApprovalAsync();

        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task ConfirmCommand_IsDisabledUntilApprovalHasBegun()
    {
        var (vm, _, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");

        Assert.False(vm.ConfirmApprovalCommand.CanExecute(null));

        vm.BeginApprovalCommand.Execute(null);

        Assert.True(vm.ConfirmApprovalCommand.CanExecute(null));
        Assert.True(vm.CancelApprovalCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelApproval_ResetsConfirmation_AndNothingIsApproved()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.BeginApproval();

        vm.CancelApproval();
        await vm.ConfirmApprovalAsync();

        Assert.False(vm.IsConfirmingApproval);
        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task ChangingTheSelection_CancelsAPendingConfirmation()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"), Samples.Stored("rec_2"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.BeginApproval();

        vm.SelectedRecommendation = Item(vm, "rec_2");
        await vm.ConfirmApprovalAsync();

        Assert.False(vm.IsConfirmingApproval);
        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task RefreshWhileConfirming_CancelsTheConfirmation()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.BeginApproval();

        await vm.RefreshAsync();
        await vm.ConfirmApprovalAsync();

        Assert.Empty(service.ApproveCalls);
    }

    [Theory]
    [InlineData(RecommendationStatus.Approved)]
    [InlineData(RecommendationStatus.Rejected)]
    [InlineData(RecommendationStatus.Superseded)]
    [InlineData(RecommendationStatus.Informational)]
    public async Task OnlyPendingRecommendationsCanBeApproved(RecommendationStatus status)
    {
        var (vm, service, _) = Create(
            Samples.Stored("rec_1", status, actionable: status != RecommendationStatus.Informational));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");

        Assert.False(vm.CanBeginApproval);

        vm.BeginApproval();
        await vm.ConfirmApprovalAsync();

        Assert.False(vm.IsConfirmingApproval);
        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task NonActionablePending_CannotBeApproved()
    {
        var (vm, service, _) = Create(
            Samples.Stored("rec_1", RecommendationStatus.Pending, actionable: false));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");

        vm.BeginApproval();
        await vm.ConfirmApprovalAsync();

        Assert.False(vm.CanBeginApproval);
        Assert.Empty(service.ApproveCalls);
    }

    [Fact]
    public async Task ApprovalFailure_IsShown_AndNoSuccessIsClaimed()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        service.ApproveFailure = new InvalidOperationException("Only pending recommendations can be approved.");
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.BeginApproval();

        await vm.ConfirmApprovalAsync();

        Assert.Equal("Only pending recommendations can be approved.", vm.ErrorMessage);
        Assert.False(vm.HasStatusMessage);
        Assert.False(vm.IsConfirmingApproval);
        Assert.False(vm.IsBusy);
        Assert.Single(service.ApproveCalls);
    }

    [Fact]
    public async Task DoubleConfirm_SubmitsOnlyOnce()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.BeginApproval();

        await vm.ConfirmApprovalAsync();
        await vm.ConfirmApprovalAsync();

        Assert.Single(service.ApproveCalls);
    }

    [Fact]
    public async Task WhileBusy_ApprovalCannotBeginOrConfirm()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.BeginApproval();

        var gate = new TaskCompletionSource<IReadOnlyList<StoredRecommendation>>();
        service.ListHandler = _ => gate.Task;
        var reload = vm.RefreshAsync();

        Assert.True(vm.IsBusy);
        Assert.False(vm.CanConfirmApproval);
        Assert.False(vm.ConfirmApprovalCommand.CanExecute(null));

        await vm.ConfirmApprovalAsync();
        Assert.Empty(service.ApproveCalls);

        gate.SetResult(service.Items.ToList());
        await reload;
    }

    // ---- Rejecting ---------------------------------------------------

    [Fact]
    public async Task Reject_PassesTheNoteAndNeverApproves()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1"));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");
        vm.RejectionNote = "Feels too slow";

        await vm.RejectAsync();

        var call = Assert.Single(service.RejectCalls);
        Assert.Equal("rec_1", call.Id);
        Assert.Equal("Feels too slow", call.Note);
        Assert.Empty(service.ApproveCalls);
        Assert.Equal(RecommendationStatus.Rejected, vm.SelectedRecommendation?.Status);
        Assert.Null(vm.RejectionNote);
    }

    [Fact]
    public async Task Reject_IsUnavailableForDecidedRecommendations()
    {
        var (vm, service, _) = Create(Samples.Stored("rec_1", RecommendationStatus.Approved));
        await vm.RefreshAsync();
        vm.SelectedRecommendation = Item(vm, "rec_1");

        await vm.RejectAsync();

        Assert.False(vm.CanReject);
        Assert.Empty(service.RejectCalls);
    }

    // ---- Navigation --------------------------------------------------

    [Fact]
    public void ViewProfiles_OnlyNavigates()
    {
        var (vm, service, _) = Create();
        PageKey? requested = null;
        vm.NavigationRequested += (_, key) => requested = key;

        vm.ViewProfilesCommand.Execute(null);

        Assert.Equal(PageKey.Profiles, requested);
        Assert.Empty(service.ApproveCalls);
    }
}
