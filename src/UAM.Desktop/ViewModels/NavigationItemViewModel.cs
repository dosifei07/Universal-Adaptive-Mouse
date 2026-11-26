using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Sidebar entry. Owns only presentation state; routing lives in
/// <see cref="MainViewModel"/>.
/// </summary>
public sealed class NavigationItemViewModel : ObservableObject
{
    private bool _isSelected;

    public NavigationItemViewModel(NavigationItem item)
    {
        Key = item.Key;
        Title = item.Title;
        Description = item.Description;
    }

    public PageKey Key { get; }

    public string Title { get; }

    public string Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
