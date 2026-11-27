namespace UAM.Desktop.ViewModels;

public enum IntegrationStatus
{
    /// <summary>Works today in this build.</summary>
    Available,

    /// <summary>Being researched; nothing is connected.</summary>
    Research,

    /// <summary>Intended for a later release.</summary>
    Planned
}

/// <summary>
/// A training-data source and its honest status. A provider is only marked
/// <see cref="IntegrationStatus.Available"/> when real code supports it.
/// </summary>
public sealed class IntegrationProviderViewModel
{
    public IntegrationProviderViewModel(
        string name,
        IntegrationStatus status,
        string description)
    {
        Name = name;
        Status = status;
        Description = description;
    }

    public string Name { get; }

    public IntegrationStatus Status { get; }

    public string Description { get; }

    public bool IsAvailable => Status == IntegrationStatus.Available;

    public string StatusText => Status switch
    {
        IntegrationStatus.Available => "Available",
        IntegrationStatus.Research => "Research only — not connected",
        IntegrationStatus.Planned => "Planned",
        _ => Status.ToString()
    };
}
