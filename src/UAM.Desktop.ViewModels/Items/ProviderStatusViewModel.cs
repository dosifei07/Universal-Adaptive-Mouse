using UAM.Application;
using UAM.Core.Models;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Presentation of one registry entry plus its honest health. Names, terms and
/// capability flags all come from <see cref="ProviderRegistry"/>; this wrapper
/// adds display text only.
/// </summary>
public sealed class ProviderStatusViewModel
{
    public ProviderStatusViewModel(
        ProviderDefinition provider,
        ProviderHealth health,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Provider = provider;
        Health = health;
        Detail = detail;
    }

    public ProviderDefinition Provider { get; }

    public ProviderHealth Health { get; }

    public string Detail { get; }

    public string Id => Provider.Id;

    public string Name => Provider.Name;

    public string Description => Provider.Description;

    public string TermsNote => Provider.TermsNote;

    public bool IsAvailable => Provider.Maturity == ProviderMaturity.Available;

    public bool CanIntegrate => Provider.CanIntegrate;

    public bool IsConnected => Health == ProviderHealth.Connected;

    public bool IsBlocked => Health == ProviderHealth.Blocked;

    public bool IsResearch => Provider.Maturity == ProviderMaturity.Research;

    public string MaturityText => Provider.Maturity switch
    {
        ProviderMaturity.Available => "Available",
        ProviderMaturity.Research => "Research only — not connected",
        ProviderMaturity.Planned => "Planned",
        _ => Provider.Maturity.ToString()
    };

    public string TermsText => Provider.Terms switch
    {
        TermsPosition.Permitted => "Terms: permitted",
        TermsPosition.PermissionRequested => "Terms: permission requested",
        TermsPosition.NotPermitted => "Terms: not permitted — no integration",
        _ => Provider.Terms.ToString()
    };

    public string HealthText => Health switch
    {
        ProviderHealth.Connected => "Connected",
        ProviderHealth.Ready => "Ready",
        ProviderHealth.NotConnected => "Not connected",
        ProviderHealth.Unavailable => "Unavailable",
        ProviderHealth.Blocked => "Blocked by terms",
        _ => Health.ToString()
    };

    public string Summary => $"{Name} · {HealthText} · {TermsText}";

    public static ProviderStatusViewModel FromDefinition(ProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var health = provider.Terms == TermsPosition.NotPermitted
            ? ProviderHealth.Blocked
            : provider.Maturity == ProviderMaturity.Available
                ? ProviderHealth.Ready
                : ProviderHealth.NotConnected;

        return new ProviderStatusViewModel(provider, health, provider.Description);
    }

    public static ProviderStatusViewModel FromSnapshot(ProviderStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ProviderStatusViewModel(
            snapshot.Provider,
            snapshot.Health,
            snapshot.Detail);
    }
}
