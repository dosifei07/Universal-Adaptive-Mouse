using UAM.Desktop.Shared.Mvvm;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// One editable gain in the profile editor. Validation here is limited to
/// "is a usable number"; it does not invent a policy range for gains.
/// </summary>
public sealed class EditableGainViewModel : ObservableObject
{
    private double _gain;

    public EditableGainViewModel(string label, string? arrow, double original)
    {
        Label = label;
        Arrow = arrow;
        Original = original;
        _gain = original;
    }

    public string Label { get; }

    public string? Arrow { get; }

    public double Original { get; }

    public double Gain
    {
        get => _gain;
        set
        {
            if (SetProperty(ref _gain, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(HasChanged));
                OnPropertyChanged(nameof(DeltaText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsValid => double.IsFinite(_gain) && _gain > 0;

    public bool HasChanged => _gain != Original;

    public string DeltaText => IsValid
        ? GainRowViewModel.DeltaFromNeutral(_gain)
        : "Invalid";

    internal event EventHandler? Changed;
}
