using UAM.Analytics;
using UAM.Core.Models;
using UAM.Core.Validation;

namespace UAM.Application;

public sealed record AnalysisResult
{
    public required PlayerModel GlobalModel { get; init; }

    public required PlayerModel GameModel { get; init; }

    public string? GameId { get; init; }

    public int TotalObservations { get; init; }

    public int AcceptedCount { get; init; }

    public int RejectedCount { get; init; }

    public required IReadOnlyList<RejectedObservation> Rejections { get; init; }

    public required IReadOnlyList<string> AvailableGames { get; init; }

    public bool HasData => GlobalModel.DirectionalEstimates.Count > 0;
}

public interface IAnalysisService
{
    Task<IReadOnlyList<string>> GetGamesAsync(
        CancellationToken cancellationToken = default);

    Task<AnalysisResult> AnalyzeAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the shared analyzer over persisted observations. The desktop and CLI
/// therefore produce identical estimates for the same database.
/// </summary>
public sealed class AnalysisService : IAnalysisService
{
    private readonly TrainingStoreFactory _storeFactory;
    private readonly UamApplicationOptions _options;
    private readonly PerformanceAnalyzer _analyzer = new();

    public AnalysisService(
        TrainingStoreFactory storeFactory,
        UamApplicationOptions options)
    {
        _storeFactory = storeFactory;
        _options = options;
    }

    public async Task<IReadOnlyList<string>> GetGamesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetGameIdsAsync(cancellationToken);
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var games = await store.GetGameIdsAsync(cancellationToken);
        var observations = await store.LoadObservationsAsync(
            cancellationToken: cancellationToken);

        var globalReport = _analyzer.AnalyzeWithDiagnostics(
            _options.PlayerId,
            observations);

        var selectedGame = string.IsNullOrWhiteSpace(gameId)
            ? games.FirstOrDefault()
            : gameId;

        PlayerModel gameModel;

        if (selectedGame is null)
        {
            gameModel = PlayerModel.Empty(_options.PlayerId);
        }
        else
        {
            gameModel = _analyzer.Analyze(
                _options.PlayerId,
                observations.Where(x => x.GameId == selectedGame));
        }

        return new AnalysisResult
        {
            GlobalModel = globalReport.Model,
            GameModel = gameModel,
            GameId = selectedGame,
            TotalObservations = observations.Count,
            AcceptedCount = globalReport.AcceptedCount,
            RejectedCount = globalReport.RejectedCount,
            Rejections = globalReport.Rejections,
            AvailableGames = games
        };
    }
}
