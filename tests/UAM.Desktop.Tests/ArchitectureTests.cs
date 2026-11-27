using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Tests;

/// <summary>
/// Structural rules for the presentation layer. They fail the build if a view
/// model starts reaching around the application services.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] References =
        typeof(MainViewModel).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();

    [Fact]
    public void ViewModels_DoNotReferenceTheDataLayer()
    {
        Assert.DoesNotContain("UAM.Data", References);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", References);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", References);
    }

    [Fact]
    public void ViewModels_DoNotReferenceTheApprovalEngine()
    {
        // Approval happens inside IOptimizationService. A reference to the
        // engine assembly would mean a view model could call the approver.
        Assert.DoesNotContain("UAM.AdaptiveEngine", References);
    }

    [Fact]
    public void ViewModels_DoNotReferenceAUiFramework()
    {
        Assert.DoesNotContain(References, x => x.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void EverySectionIsBackedByAPageViewModel()
    {
        var pageTypes = typeof(MainViewModel).Assembly
            .GetTypes()
            .Where(x => !x.IsAbstract && typeof(PageViewModelBase).IsAssignableFrom(x))
            .ToList();

        Assert.Equal(7, pageTypes.Count);
    }
}
