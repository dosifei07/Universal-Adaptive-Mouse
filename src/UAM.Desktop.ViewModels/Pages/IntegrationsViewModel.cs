using System.Collections.ObjectModel;
using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Lists training-data sources and what actually works today. The legacy
/// <see cref="Providers"/> list is kept for compatibility; the full
/// <see cref="Registry"/> adds terms positions and honest health. UAM Lab is a
/// child view model on this page, not an eighth top-level page.
/// </summary>
public sealed class IntegrationsViewModel : PageViewModelBase
{
    private readonly IProviderHealthService? _healthService;

    public IntegrationsViewModel()
        : this(null, null)
    {
    }

    public IntegrationsViewModel(ILabSessionService? labService)
        : this(labService, null)
    {
    }

    public IntegrationsViewModel(
        ILabSessionService? labService,
        IProviderHealthService? healthService)
        : base(PageKey.Integrations)
    {
        _healthService = healthService;
        Lab = new LabViewModel(labService);

        Providers =
        [
            new IntegrationProviderViewModel(
                "Training JSON import",
                IntegrationStatus.Available,
                "Import a versioned, provider-neutral training document from a " +
                "file or the watched imports folder. Re-importing the same " +
                "content creates no duplicate observations."),
            new IntegrationProviderViewModel(
                "Aimlabs",
                IntegrationStatus.Research,
                "Researching how Aimlabs session data could be exported into the " +
                "canonical format. UAM is not connected to Aimlabs and " +
                "reads no data from it."),
            new IntegrationProviderViewModel(
                "Other training providers",
                IntegrationStatus.Planned,
                "Any provider that can produce a permitted canonical training " +
                "document can be added in a later release.")
        ];

        foreach (var provider in ProviderRegistry.All)
        {
            Registry.Add(ProviderStatusViewModel.FromDefinition(provider));
        }

        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(),
            () => !IsBusy && _healthService is not null);
    }

    public IReadOnlyList<IntegrationProviderViewModel> Providers { get; }

    public ObservableCollection<ProviderStatusViewModel> Registry { get; } = [];

    public LabViewModel Lab { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public string Notice =>
        "Only providers with permitted terms and real code are importable. " +
        "UAM does not read game telemetry or change mouse behavior.";

    public bool HasAvailableProvider => Providers.Any(x => x.IsAvailable);

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        // Without a health service this page is entirely static, which keeps
        // the parameterless constructor usable without data or an error.
        return _healthService is null
            ? Task.CompletedTask
            : RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_healthService is null)
        {
            return Task.CompletedTask;
        }

        return LoadLatestAsync(
            token => _healthService.GetStatusesAsync(token),
            ApplyStatuses,
            cancellationToken);
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void ApplyStatuses(IReadOnlyList<ProviderStatusSnapshot> statuses)
    {
        Registry.Clear();

        foreach (var status in statuses)
        {
            Registry.Add(ProviderStatusViewModel.FromSnapshot(status));
        }

        OnPropertyChanged(nameof(Registry));
        OnPropertyChanged(nameof(Notice));
    }
}
