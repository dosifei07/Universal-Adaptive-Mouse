using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.ViewModels;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.Tests;

internal sealed class FakeDashboardService : IDashboardService
{
    public Func<CancellationToken, Task<DashboardSummary>> Handler { get; set; } =
        _ => Task.FromResult(Samples.EmptyDashboard());

    public int Calls { get; private set; }

    public Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Handler(cancellationToken);
    }
}

internal sealed class FakeAnalysisService : IAnalysisService
{
    public Func<string?, CancellationToken, Task<AnalysisResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(Samples.Analysis());

    public Func<CancellationToken, Task<IReadOnlyList<string>>> GamesHandler { get; set; } =
        _ => Task.FromResult<IReadOnlyList<string>>(["valorant", "cs2"]);

    public List<string?> RequestedGames { get; } = [];

    public Task<IReadOnlyList<string>> GetGamesAsync(CancellationToken cancellationToken = default)
    {
        return GamesHandler(cancellationToken);
    }

    public Task<AnalysisResult> AnalyzeAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        RequestedGames.Add(gameId);
        return Handler(gameId, cancellationToken);
    }
}

internal sealed class FakeOptimizationService : IOptimizationService
{
    public List<StoredRecommendation> Items { get; } = [];

    public Exception? GenerateFailure { get; set; }

    public Exception? ApproveFailure { get; set; }

    public Func<StoredRecommendation>? GenerateResult { get; set; }

    public List<string?> GenerateCalls { get; } = [];

    public List<string> ApproveCalls { get; } = [];

    public List<(string Id, string? Note)> RejectCalls { get; } = [];

    public Func<CancellationToken, Task<IReadOnlyList<StoredRecommendation>>>? ListHandler { get; set; }

    public Task<IReadOnlyList<StoredRecommendation>> GetRecommendationsAsync(
        RecommendationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        if (ListHandler is not null)
        {
            return ListHandler(cancellationToken);
        }

        IReadOnlyList<StoredRecommendation> result = Items
            .Where(x => status is null || x.Status == status)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<StoredRecommendation> GenerateAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        GenerateCalls.Add(gameId);

        if (GenerateFailure is not null)
        {
            return Task.FromException<StoredRecommendation>(GenerateFailure);
        }

        var stored = GenerateResult?.Invoke()
            ?? Samples.Stored("rec_new", gameId: gameId ?? "valorant");

        Items.Insert(0, stored);
        return Task.FromResult(stored);
    }

    public Task<StoredRecommendation> ApproveAsync(
        string recommendationId,
        CancellationToken cancellationToken = default)
    {
        ApproveCalls.Add(recommendationId);

        if (ApproveFailure is not null)
        {
            return Task.FromException<StoredRecommendation>(ApproveFailure);
        }

        var index = Items.FindIndex(x => x.Id == recommendationId);
        var updated = Items[index] with { Status = RecommendationStatus.Approved };
        Items[index] = updated;
        return Task.FromResult(updated);
    }

    public Task<StoredRecommendation> RejectAsync(
        string recommendationId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        RejectCalls.Add((recommendationId, note));

        var index = Items.FindIndex(x => x.Id == recommendationId);
        var updated = Items[index] with
        {
            Status = RecommendationStatus.Rejected,
            DecisionNote = note
        };
        Items[index] = updated;
        return Task.FromResult(updated);
    }
}

internal sealed class FakeProfileService : IProfileService
{
    public List<ApprovedProfile> Items { get; } = [];

    public Func<CancellationToken, Task<IReadOnlyList<ApprovedProfile>>>? ListHandler { get; set; }

    public Exception? ExportFailure { get; set; }

    public Exception? SaveFailure { get; set; }

    public List<(string GameId, string Path)> ExportCalls { get; } = [];

    public List<(string GameId, GameProfile Profile)> SaveCalls { get; } = [];

    public Task<IReadOnlyList<ApprovedProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        if (ListHandler is not null)
        {
            return ListHandler(cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ApprovedProfile>>(Items.ToList());
    }

    public Task<ApprovedProfile?> GetProfileAsync(
        string gameId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Items
            .Where(x => x.GameId == gameId)
            .OrderByDescending(x => x.ApprovedAtUtc)
            .FirstOrDefault());
    }

    public Task<ApprovedProfile> SaveEditedProfileAsync(
        string gameId,
        GameProfile editedProfile,
        CancellationToken cancellationToken = default)
    {
        SaveCalls.Add((gameId, editedProfile));

        if (SaveFailure is not null)
        {
            return Task.FromException<ApprovedProfile>(SaveFailure);
        }

        var saved = new ApprovedProfile
        {
            Id = "prf_edited_" + SaveCalls.Count,
            GameId = gameId,
            RecommendationId = "rec_x",
            Profile = editedProfile,
            ApprovedAtUtc = Samples.Now.AddMinutes(SaveCalls.Count),
            IsManuallyEdited = true
        };

        Items.Add(saved);
        return Task.FromResult(saved);
    }

    public Task<string> ExportAsync(
        string gameId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ExportCalls.Add((gameId, destinationPath));

        return ExportFailure is not null
            ? Task.FromException<string>(ExportFailure)
            : Task.FromResult(Path.GetFullPath(destinationPath));
    }
}

internal sealed class FakeHistoryService : IHistoryService
{
    public List<TrainingSessionSummary> Sessions { get; } = [];

    public Exception? Failure { get; set; }

    public List<(string? Game, string? Scenario)> SessionRequests { get; } = [];

    public Task<IReadOnlyList<TrainingSessionSummary>> GetSessionsAsync(
        string? gameId = null,
        string? scenarioId = null,
        CancellationToken cancellationToken = default)
    {
        SessionRequests.Add((gameId, scenarioId));

        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<TrainingSessionSummary>>(Failure);
        }

        IReadOnlyList<TrainingSessionSummary> result = Sessions
            .Where(x => gameId is null || x.GameId == gameId)
            .Where(x => scenarioId is null || x.ScenarioId == scenarioId)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> GetGamesAsync(
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<string>>(Failure);
        }

        IReadOnlyList<string> games = Sessions
            .Select(x => x.GameId)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        return Task.FromResult(games);
    }

    public Task<IReadOnlyList<string>> GetScenariosAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> scenarios = Sessions
            .Where(x => gameId is null || x.GameId == gameId)
            .Select(x => x.ScenarioId)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        return Task.FromResult(scenarios);
    }
}

/// <summary>Minimal page for exercising <see cref="MainViewModel"/> routing.</summary>
internal sealed class StubPage : PageViewModelBase
{
    public StubPage(PageKey key) : base(key)
    {
    }

    public int Activations { get; private set; }

    public CancellationToken LastToken { get; private set; }

    public bool Unsaved { get; set; }

    public Func<CancellationToken, Task>? ActivationHandler { get; set; }

    public override bool HasUnsavedChanges => Unsaved;

    public override async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        Activations++;
        LastToken = cancellationToken;

        if (ActivationHandler is not null)
        {
            await ActivationHandler(cancellationToken);
        }
    }

    public void Request(PageKey key) => RequestNavigation(key);

    public static List<StubPage> All() =>
        NavigationCatalog.Items.Select(x => new StubPage(x.Key)).ToList();
}
