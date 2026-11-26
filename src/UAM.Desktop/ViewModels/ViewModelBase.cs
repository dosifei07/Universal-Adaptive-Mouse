using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Base for every page view model. It centralizes busy state and error
/// reporting so pages do not duplicate try/catch blocks.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _errorMessage;

    public event EventHandler<PageKey>? NavigationRequested;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        protected set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public virtual Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected async Task RunSafeAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected void RequestNavigation(PageKey key)
    {
        NavigationRequested?.Invoke(this, key);
    }
}
