using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;

namespace UAM.Desktop.ViewModels;

/// <summary>Display wrapper for an approved profile.</summary>
public sealed class ProfileItemViewModel
{
    public ProfileItemViewModel(ApprovedProfile approved)
    {
        ArgumentNullException.ThrowIfNull(approved);

        Approved = approved;

        DirectionalGains = GainRowViewModel.DirectionalRows(
            approved.Profile.DirectionalGains);
        SpeedGains = GainRowViewModel.SpeedRows(approved.Profile.SpeedGains);
    }

    public ApprovedProfile Approved { get; }

    public string GameId => Approved.GameId;

    public GameProfile Profile => Approved.Profile;

    public string ApprovedBy => Approved.ApprovedBy;

    public string ApprovedText => UamFormat.Date(Approved.ApprovedAtUtc);

    public bool IsManuallyEdited => Approved.IsManuallyEdited;

    public string OriginText => Approved.IsManuallyEdited
        ? "Manually edited after approval"
        : "Approved recommendation";

    public string BaseSensitivityText => UamFormat.Gain(Approved.Profile.BaseSensitivity);

    public string DpiText => Approved.Profile.Dpi > 0
        ? UamFormat.Count((int)Math.Round(Approved.Profile.Dpi))
        : "Not set";

    public string InGameSensitivityText => Approved.Profile.InGameSensitivity > 0
        ? Approved.Profile.InGameSensitivity.ToString(
            "0.###",
            System.Globalization.CultureInfo.CurrentCulture)
        : "Not set";

    public IReadOnlyList<GainRowViewModel> DirectionalGains { get; }

    public IReadOnlyList<GainRowViewModel> SpeedGains { get; }
}
