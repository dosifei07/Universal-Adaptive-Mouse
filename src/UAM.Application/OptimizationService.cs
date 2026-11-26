using UAM.AdaptiveEngine;
using UAM.Core.Models;

namespace UAM.Application;

public interface IOptimizationService
{
    Task<IReadOnlyList<StoredRecommendation>> GetRecommendationsAsync(
        RecommendationStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<StoredRecommendation> GenerateAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default);

    Task<StoredRecommendation> ApproveAsync(
        string recommendationId,
        CancellationToken cancellationToken = default);

    Task<StoredRecommendation> RejectAsync(
        string recommendationId,
        string? note = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the recommendation workflow. Approval always goes through
/// <see cref="RecommendationApprover"/>; the desktop app cannot bypass it.
/// </summary>
public sealed class OptimizationService : IOptimizationService
{
    private readonly TrainingStoreFactory _storeFactory;
    private readonly IAnalysisService _analysisService;
    private readonly UamApplicationOptions _options;
    private readonly OptimizationEngine _engine = new();
    private readonly RecommendationApprover _approver = new();

    public OptimizationService(
        TrainingStoreFactory storeFactory,
        IAnalysisService analysisService,
        UamApplicationOptions options)
    {
        _storeFactory = storeFactory;
        _analysisService = analysisService;
        _options = options;
    }

    public async Task<IReadOnlyList<StoredRecommendation>> GetRecommendationsAsync(
        RecommendationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);
        return await store.GetRecommendationsAsync(
            status,
            cancellationToken: cancellationToken);
    }

    public async Task<StoredRecommendation> GenerateAsync(
        string? gameId = null,
        CancellationToken cancellationToken = default)
    {
        var analysis = await _analysisService.AnalyzeAsync(
            gameId,
            cancellationToken);

        if (analysis.GameId is null)
        {
            throw new InvalidOperationException(
                "No training data is stored yet. Import a session before " +
                "generating a recommendation.");
        }

        var recommendation = _engine.BuildRecommendation(
            analysis.GameId,
            analysis.GlobalModel,
            analysis.GameModel);

        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var previous = await store.GetRecommendationsAsync(
            RecommendationStatus.Pending,
            analysis.GameId,
            cancellationToken);

        foreach (var item in previous)
        {
            await store.UpdateRecommendationStatusAsync(
                item.Id,
                RecommendationStatus.Superseded,
                "Replaced by a newer recommendation.",
                cancellationToken);
        }

        var stored = new StoredRecommendation
        {
            Id = $"rec_{Guid.NewGuid():N}",
            Recommendation = recommendation,
            Status = recommendation.IsActionable
                ? RecommendationStatus.Pending
                : RecommendationStatus.Informational,
            StoredAtUtc = DateTime.UtcNow
        };

        await store.SaveRecommendationAsync(stored, cancellationToken);
        return stored;
    }

    public async Task<StoredRecommendation> ApproveAsync(
        string recommendationId,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var stored = await store.GetRecommendationAsync(
            recommendationId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Recommendation '{recommendationId}' was not found.");

        if (stored.Status != RecommendationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Only pending recommendations can be approved. Current status: " +
                $"{stored.Status}.");
        }

        var approvedProfile = _approver.Approve(
            stored.Recommendation,
            _options.ApprovedBy);

        var approved = new ApprovedProfile
        {
            Id = $"prf_{Guid.NewGuid():N}",
            GameId = stored.Recommendation.GameId,
            RecommendationId = stored.Id,
            Profile = approvedProfile,
            ApprovedBy = _options.ApprovedBy,
            ApprovedAtUtc = DateTime.UtcNow
        };

        await store.SaveApprovedProfileAsync(approved, cancellationToken);

        var updated = await store.UpdateRecommendationStatusAsync(
            stored.Id,
            RecommendationStatus.Approved,
            $"Approved by {_options.ApprovedBy}.",
            cancellationToken);

        return updated ?? stored with { Status = RecommendationStatus.Approved };
    }

    public async Task<StoredRecommendation> RejectAsync(
        string recommendationId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await using var store = await _storeFactory.CreateAsync(cancellationToken);

        var stored = await store.GetRecommendationAsync(
            recommendationId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Recommendation '{recommendationId}' was not found.");

        if (stored.Status != RecommendationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Only pending recommendations can be rejected. Current status: " +
                $"{stored.Status}.");
        }

        var updated = await store.UpdateRecommendationStatusAsync(
            stored.Id,
            RecommendationStatus.Rejected,
            string.IsNullOrWhiteSpace(note) ? "Rejected by user." : note,
            cancellationToken);

        return updated ?? stored with { Status = RecommendationStatus.Rejected };
    }
}
