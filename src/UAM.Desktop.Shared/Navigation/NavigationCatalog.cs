namespace UAM.Desktop.Shared.Navigation;

/// <summary>
/// The seven top-level destinations required by the v0.2 interface.
/// </summary>
public enum PageKey
{
    Dashboard,
    Analysis,
    Optimization,
    Profiles,
    History,
    Integrations,
    Settings
}

public sealed record NavigationItem(
    PageKey Key,
    string Title,
    string Description);

public static class NavigationCatalog
{
    public static IReadOnlyList<NavigationItem> Items { get; } =
    [
        new(
            PageKey.Dashboard,
            "Dashboard",
            "Your aiming performance overview."),
        new(
            PageKey.Analysis,
            "Aim Analysis",
            "Directional and speed weaknesses with confidence."),
        new(
            PageKey.Optimization,
            "Optimization",
            "Proposed adjustments and the approval workflow."),
        new(
            PageKey.Profiles,
            "Game Profiles",
            "Per-game configurations and approved exports."),
        new(
            PageKey.History,
            "Training History",
            "Stored sessions, scenarios, and trends."),
        new(
            PageKey.Integrations,
            "Integrations",
            "Aimlabs research and future training providers."),
        new(
            PageKey.Settings,
            "Settings",
            "Local database, player identity, and application state.")
    ];

    public static NavigationItem Get(PageKey key)
    {
        return Items.First(item => item.Key == key);
    }
}
