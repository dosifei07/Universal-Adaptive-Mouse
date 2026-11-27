using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Base for every page view model. It centralizes busy state and error
/// reporting so pages do not duplicate try/catch blocks.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private int _busyDepth;
    private bool _isBusy;
    private string? _errorMessage;

    public event EventHandler<PageKey>? NavigationRequested;

    /// <summary>
    /// True while at least one operation started through
    /// <see cref="RunSafeAsync"/> is running. Overlapping operations keep the
    /// flag set until the last one finishes.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnBusyChanged();
            }
        }
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

    /// <summary>
    /// Runs <paramref name="action"/> with busy and error handling. A failure
    /// is reported through <see cref="ErrorMessage"/> instead of escaping to
    /// the UI thread. Cancellation is not a failure and is not reported.
    /// </summary>
    protected async Task RunSafeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _busyDepth++;
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // A superseded or cancelled operation is not an error.
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _busyDepth--;
            IsBusy = _busyDepth > 0;
        }
    }

    /// <summary>
    /// Called after <see cref="IsBusy"/> changes so derived classes can
    /// refresh command availability.
    /// </summary>
    protected virtual void OnBusyChanged()
    {
    }

    protected void RequestNavigation(PageKey key)
    {
        NavigationRequested?.Invoke(this, key);
    }
}
