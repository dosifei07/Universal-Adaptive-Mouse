using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;

namespace UAM.Desktop.ViewModels;

/// <summary>One stored training session in the history table.</summary>
public sealed class SessionRowViewModel
{
    public SessionRowViewModel(TrainingSessionSummary session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        Duration = session.LastObservationUtc - session.FirstObservationUtc;
    }

    public TrainingSessionSummary Session { get; }

    public string SessionId => Session.SessionId;

    public string GameId => Session.GameId;

    public string ScenarioId => Session.ScenarioId;

    public DateTime StartedUtc => Session.FirstObservationUtc;

    public DateTime EndedUtc => Session.LastObservationUtc;

    public TimeSpan Duration { get; }

    public string StartedText => UamFormat.Date(Session.FirstObservationUtc);

    public string DurationText => UamFormat.Duration(Duration);

    public int ObservationCount => Session.ObservationCount;

    public string ObservationCountText => UamFormat.Count(Session.ObservationCount);
}
