using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Lists training-data sources and what actually works today. Nothing here
/// claims a connection that does not exist; unfinished providers are labeled
/// as research or planned.
/// </summary>
public sealed class IntegrationsViewModel : PageViewModelBase
{
    public IntegrationsViewModel()
        : base(PageKey.Integrations)
    {
        Providers =
        [
            new IntegrationProviderViewModel(
                "Training JSON import",
                IntegrationStatus.Available,
                "Import a provider-neutral training session file with the UAM " +
                "command-line tool (analyze <file> --save). Stored sessions then " +
                "appear on every page."),
            new IntegrationProviderViewModel(
                "Aimlabs",
                IntegrationStatus.Research,
                "Researching how Aimlabs session data could be exported into the " +
                "provider-neutral format. UAM is not connected to Aimlabs and " +
                "reads no data from it."),
            new IntegrationProviderViewModel(
                "Other training providers",
                IntegrationStatus.Planned,
                "Any provider that can produce the provider-neutral training " +
                "observation format can be added in a later release.")
        ];
    }

    public IReadOnlyList<IntegrationProviderViewModel> Providers { get; }

    public string Notice =>
        "This version analyzes imported training data only. It does not read " +
        "from games or change mouse behavior.";

    public bool HasAvailableProvider => Providers.Any(x => x.IsAvailable);
}
