using UAM.Application;
using UAM.Core.Models;
using UAM.Data;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

/// <summary>
/// The view models running against the real application services and a real
/// SQLite file. Nothing in this class is faked below the view models, so it
/// proves the approval workflow works end to end.
/// </summary>
public sealed class WorkflowIntegrationTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;
    private readonly UamApplicationOptions _options;

    public WorkflowIntegrationTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "uam-desktop-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "uam.db");
        _options = new UamApplicationOptions
        {
            DatabasePath = _databasePath,
            ApprovedBy = "workflow-test"
        };
    }

    private sealed record Pages(
        DashboardViewModel Dashboard,
        AnalysisViewModel Analysis,
        OptimizationViewModel Optimization,
        ProfilesViewModel Profiles,
        HistoryViewModel History,
        SettingsViewModel Settings,
        IProfileService ProfileService);

    private Pages CreatePages()
    {
        var factory = new TrainingStoreFactory(_databasePath);
        var analysis = new AnalysisService(factory, _options);
        var dashboard = new DashboardService(factory, _options);
        var history = new HistoryService(factory);
        var optimization = new OptimizationService(factory, analysis, _options);
        var profiles = new ProfileService(factory, _options);

        return new Pages(
            new DashboardViewModel(dashboard),
            new AnalysisViewModel(analysis),
            new OptimizationViewModel(optimization, analysis),
            new ProfilesViewModel(profiles, Path.Combine(_directory, "exports")),
            new HistoryViewModel(history),
            new SettingsViewModel(_options, dashboard),
            profiles);
    }

    private async Task SeedAsync(int observations = 240)
    {
        var start = DateTime.UtcNow.AddHours(-2);

        var data = Enumerable.Range(0, observations)
            .Select(i => new TrainingObservation
            {
                SessionId = "session-001",
                GameId = "valorant",
                ScenarioId = "gridshot",
                TimestampUtc = start.AddSeconds(i),
                DirectionDegrees = 0,
                SpeedPixelsPerSecond = 650,
                SignedErrorRatio = 0.5,
                AcquisitionTimeMs = 400,
                Hit = false,
                MovementKind = MovementKind.Flick
            })
            .ToList();

        await using var store = new TrainingStore(_databasePath);
        await store.InitializeAsync();
        var result = await store.SaveObservationsAsync(data);
        Assert.Equal(observations, result.SavedCount);
    }

    [Fact]
    public async Task EmptyDatabase_EveryPageLoadsWithoutErrorAndWithoutInventedData()
    {
        var pages = CreatePages();

        await pages.Dashboard.OnActivatedAsync();
        await pages.Analysis.OnActivatedAsync();
        await pages.Optimization.OnActivatedAsync();
        await pages.Profiles.OnActivatedAsync();
        await pages.History.OnActivatedAsync();
        await pages.Settings.OnActivatedAsync();

        Assert.False(pages.Dashboard.HasError);
        Assert.True(pages.Dashboard.IsEmpty);
        Assert.True(pages.Analysis.IsEmpty);
        Assert.True(pages.Optimization.NeedsTrainingData);
        Assert.True(pages.Profiles.IsEmpty);
        Assert.True(pages.History.IsEmpty);
        Assert.Equal("Connected", pages.Settings.DatabaseStatus);
        Assert.False(pages.Optimization.HasError);
        Assert.False(pages.Settings.HasError);
    }

    [Fact]
    public async Task Dashboard_ShowsTheCountsAndEstimatesStoredInSqlite()
    {
        await SeedAsync(240);
        var pages = CreatePages();

        await pages.Dashboard.OnActivatedAsync();

        Assert.True(pages.Dashboard.HasData);
        Assert.Equal("240", new string(pages.Dashboard.ObservationCountText.Where(char.IsDigit).ToArray()));
        Assert.Equal("1", pages.Dashboard.SessionCountText);
        Assert.Equal("1", pages.Dashboard.GameCountText);

        var right = pages.Dashboard.Heatmap[0];
        Assert.True(right.HasData);
        Assert.Equal(240, right.SampleCount);
        Assert.Equal(0.5, right.MeanErrorRatio, 6);
        Assert.False(pages.Dashboard.Heatmap[4].HasData);
    }

    [Fact]
    public async Task History_ListsTheStoredSession()
    {
        await SeedAsync(240);
        var pages = CreatePages();

        await pages.History.OnActivatedAsync();

        var row = Assert.Single(pages.History.Sessions);
        Assert.Equal("session-001", row.SessionId);
        Assert.Equal(240, row.ObservationCount);
    }

    [Fact]
    public async Task FullWorkflow_GenerateReviewApproveExport()
    {
        await SeedAsync(240);
        var pages = CreatePages();

        // Generate: produces a pending recommendation and nothing else.
        await pages.Optimization.OnActivatedAsync();
        await pages.Optimization.GenerateAsync();

        var recommendation = pages.Optimization.SelectedRecommendation;
        Assert.NotNull(recommendation);
        Assert.Equal(RecommendationStatus.Pending, recommendation!.Status);
        Assert.True(recommendation.CanApprove);

        // Before approval there is no approved profile and no export path.
        await pages.Profiles.OnActivatedAsync();
        Assert.True(pages.Profiles.IsEmpty);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.ProfileService.ExportAsync(
                "valorant",
                Path.Combine(_directory, "premature.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "premature.json")));

        // Approve: two explicit steps.
        pages.Optimization.BeginApproval();
        Assert.True(pages.Optimization.IsConfirmingApproval);
        await pages.Optimization.ConfirmApprovalAsync();

        Assert.False(pages.Optimization.HasError);
        Assert.Equal(RecommendationStatus.Approved, pages.Optimization.SelectedRecommendation?.Status);

        // The approved profile now appears and exports.
        await pages.Profiles.OnActivatedAsync();
        var profile = Assert.Single(pages.Profiles.Profiles);
        Assert.Equal("valorant", profile.GameId);
        Assert.Equal("workflow-test", profile.ApprovedBy);
        Assert.False(profile.IsManuallyEdited);

        pages.Profiles.SelectedProfile = profile;
        await pages.Profiles.ExportAsync();

        Assert.False(pages.Profiles.HasError);
        Assert.True(File.Exists(pages.Profiles.ExportPath));
        Assert.Contains("valorant", await File.ReadAllTextAsync(pages.Profiles.ExportPath));

        // The dashboard reflects the approval and no pending work remains.
        await pages.Dashboard.OnActivatedAsync();
        Assert.True(pages.Dashboard.HasActiveProfile);
        Assert.False(pages.Dashboard.HasPendingRecommendation);
    }

    [Fact]
    public async Task ApprovedRecommendation_CannotBeApprovedAgain()
    {
        await SeedAsync(240);
        var pages = CreatePages();
        await pages.Optimization.OnActivatedAsync();
        await pages.Optimization.GenerateAsync();
        pages.Optimization.BeginApproval();
        await pages.Optimization.ConfirmApprovalAsync();

        pages.Optimization.BeginApproval();

        Assert.False(pages.Optimization.CanBeginApproval);
        Assert.False(pages.Optimization.IsConfirmingApproval);
    }

    [Fact]
    public async Task RejectedRecommendation_CreatesNoProfile()
    {
        await SeedAsync(240);
        var pages = CreatePages();
        await pages.Optimization.OnActivatedAsync();
        await pages.Optimization.GenerateAsync();

        await pages.Optimization.RejectAsync();
        await pages.Profiles.OnActivatedAsync();

        Assert.Equal(RecommendationStatus.Rejected, pages.Optimization.SelectedRecommendation?.Status);
        Assert.True(pages.Profiles.IsEmpty);
    }

    [Fact]
    public async Task EditedProfile_IsSavedAsANewVersionAndShownAsEdited()
    {
        await SeedAsync(240);
        var pages = CreatePages();
        await pages.Optimization.OnActivatedAsync();
        await pages.Optimization.GenerateAsync();
        pages.Optimization.BeginApproval();
        await pages.Optimization.ConfirmApprovalAsync();

        await pages.Profiles.OnActivatedAsync();
        pages.Profiles.SelectedProfile = pages.Profiles.Profiles[0];
        pages.Profiles.BeginEdit();
        pages.Profiles.EditDpi = 1600;
        await pages.Profiles.SaveEditAsync();

        Assert.False(pages.Profiles.HasError);
        var profile = Assert.Single(pages.Profiles.Profiles);
        Assert.True(profile.IsManuallyEdited);
        Assert.Equal(1600, profile.Profile.Dpi);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; the temp directory is disposable.
        }
    }
}
