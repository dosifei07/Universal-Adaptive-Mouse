using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Display wrapper for a stored recommendation. Approvability comes from the
/// domain record itself (<see cref="StoredRecommendation.CanApprove"/>); the
/// view model never decides it independently.
/// </summary>
public sealed class RecommendationItemViewModel
{
    public RecommendationItemViewModel(StoredRecommendation stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        Stored = stored;

        DirectionalGains = GainRowViewModel.DirectionalRows(
            stored.Recommendation.ProposedProfile.DirectionalGains);
        SpeedGains = GainRowViewModel.SpeedRows(
            stored.Recommendation.ProposedProfile.SpeedGains);
    }

    public StoredRecommendation Stored { get; }

    public string Id => Stored.Id;

    public string GameId => Stored.GameId;

    public RecommendationStatus Status => Stored.Status;

    public string StatusText => UamFormat.Status(Stored.Status);

    public bool IsPending => Stored.Status == RecommendationStatus.Pending;

    public bool IsActionable => Stored.Recommendation.IsActionable;

    public bool CanApprove => Stored.CanApprove;

    public string ConfidenceText => UamFormat.Confidence(Stored.Recommendation.Confidence);

    public string Reason => Stored.Recommendation.Reason;

    public string CreatedText => UamFormat.Date(Stored.Recommendation.CreatedAtUtc);

    public string DecidedText => UamFormat.Date(Stored.DecidedAtUtc);

    public string? DecisionNote => Stored.DecisionNote;

    public bool HasDecision => Stored.DecidedAtUtc is not null;

    public IReadOnlyList<GainRowViewModel> DirectionalGains { get; }

    public IReadOnlyList<GainRowViewModel> SpeedGains { get; }

    public string Summary => $"{GameId} · {StatusText} · {CreatedText}";
}
