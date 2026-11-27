using System.Collections.ObjectModel;
using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Formatting;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Approved per-game profiles, manual edits of approved profiles, and export.
/// Everything here has already crossed the approval boundary; pending
/// recommendations are not reachable from this page.
/// </summary>
public sealed class ProfilesViewModel : PageViewModelBase
{
    private readonly IProfileService _service;
    private readonly string _exportDirectory;

    private ProfileItemViewModel? _selected;
    private string? _pendingSelectionGameId;
    private bool _isEditing;
    private double _editDpi;
    private double _editInGameSensitivity;
    private int[] _directionalKeys = [];
    private SpeedBand[] _speedKeys = [];
    private string _exportPath = string.Empty;
    private string? _statusMessage;

    public ProfilesViewModel(IProfileService service, string? exportDirectory = null)
        : base(PageKey.Profiles)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _exportDirectory = string.IsNullOrWhiteSpace(exportDirectory)
            ? Path.GetFullPath("exports")
            : exportDirectory;

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsBusy && !IsEditing);
        BeginEditCommand = new RelayCommand(BeginEdit, () => CanBeginEdit);
        SaveEditCommand = new AsyncRelayCommand(() => SaveEditAsync(), () => CanSaveEdit);
        DiscardEditCommand = new RelayCommand(DiscardEdit, () => IsEditing);
        ExportCommand = new AsyncRelayCommand(() => ExportAsync(), () => CanExport);
        ViewOptimizationCommand = new RelayCommand(
            () => RequestNavigation(PageKey.Optimization));
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand BeginEditCommand { get; }

    public AsyncRelayCommand SaveEditCommand { get; }

    public RelayCommand DiscardEditCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public RelayCommand ViewOptimizationCommand { get; }

    /// <summary>The latest approved profile for each game.</summary>
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    public ObservableCollection<EditableGainViewModel> EditDirectionalGains { get; } = [];

    public ObservableCollection<EditableGainViewModel> EditSpeedGains { get; } = [];

    public ProfileItemViewModel? SelectedProfile
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            if (_isEditing)
            {
                // Do not silently discard an edit in progress; tell the view
                // to snap back to the profile being edited.
                OnPropertyChanged(nameof(SelectedProfile));
                return;
            }

            _selected = value;
            ResetExportPath();
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(HasSelection));
            UpdateCommands();
        }
    }

    public bool HasSelection => _selected is not null;

    public bool IsEmpty => HasLoaded && Profiles.Count == 0 && !HasError;

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateCommands();
            }
        }
    }

    public double EditDpi
    {
        get => _editDpi;
        set
        {
            if (SetProperty(ref _editDpi, value))
            {
                OnEditorChanged();
            }
        }
    }

    public double EditInGameSensitivity
    {
        get => _editInGameSensitivity;
        set
        {
            if (SetProperty(ref _editInGameSensitivity, value))
            {
                OnEditorChanged();
            }
        }
    }

    public override bool HasUnsavedChanges =>
        _isEditing && _selected is not null &&
        (EditDirectionalGains.Any(x => x.HasChanged) ||
         EditSpeedGains.Any(x => x.HasChanged) ||
         _editDpi != _selected.Profile.Dpi ||
         _editInGameSensitivity != _selected.Profile.InGameSensitivity);

    public bool EditorIsValid =>
        EditDirectionalGains.All(x => x.IsValid) &&
        EditSpeedGains.All(x => x.IsValid) &&
        double.IsFinite(_editDpi) && _editDpi >= 0 &&
        double.IsFinite(_editInGameSensitivity) && _editInGameSensitivity >= 0;

    public string ExportPath
    {
        get => _exportPath;
        set
        {
            if (SetProperty(ref _exportPath, value ?? string.Empty))
            {
                UpdateCommands();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    public bool CanBeginEdit => !IsBusy && !_isEditing && _selected is not null;

    public bool CanSaveEdit => !IsBusy && _isEditing && HasUnsavedChanges;

    public bool CanExport =>
        !IsBusy && !_isEditing && _selected is not null &&
        !string.IsNullOrWhiteSpace(_exportPath);

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isEditing)
        {
            // Reloading would replace the profile under the editor.
            return Task.CompletedTask;
        }

        return LoadLatestAsync(
            token => _service.GetProfilesAsync(token),
            Apply,
            cancellationToken);
    }

    public void BeginEdit()
    {
        if (!CanBeginEdit || _selected is null)
        {
            return;
        }

        var profile = _selected.Profile;

        EditDirectionalGains.Clear();
        EditSpeedGains.Clear();

        var directional = profile.DirectionalGains.OrderBy(x => x.Key).ToList();
        _directionalKeys = directional.Select(x => x.Key).ToArray();

        foreach (var pair in directional)
        {
            EditDirectionalGains.Add(Track(new EditableGainViewModel(
                UamFormat.Direction(pair.Key),
                UamFormat.DirectionArrow(pair.Key),
                pair.Value)));
        }

        var speed = profile.SpeedGains.OrderBy(x => (int)x.Key).ToList();
        _speedKeys = speed.Select(x => x.Key).ToArray();

        foreach (var pair in speed)
        {
            EditSpeedGains.Add(Track(new EditableGainViewModel(
                UamFormat.Band(pair.Key),
                null,
                pair.Value)));
        }

        _editDpi = profile.Dpi;
        _editInGameSensitivity = profile.InGameSensitivity;
        OnPropertyChanged(nameof(EditDpi));
        OnPropertyChanged(nameof(EditInGameSensitivity));

        StatusMessage = null;
        IsEditing = true;
        OnEditorChanged();
    }

    public void DiscardEdit()
    {
        EditDirectionalGains.Clear();
        EditSpeedGains.Clear();
        _directionalKeys = [];
        _speedKeys = [];

        IsEditing = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        UpdateCommands();
    }

    public async Task SaveEditAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSaveEdit || _selected is null)
        {
            return;
        }

        if (!EditorIsValid)
        {
            ErrorMessage =
                "Every gain must be a positive number, and DPI and in-game " +
                "sensitivity cannot be negative.";
            return;
        }

        var gameId = _selected.GameId;

        var edited = _selected.Profile with
        {
            Dpi = _editDpi,
            InGameSensitivity = _editInGameSensitivity,
            DirectionalGains = _directionalKeys
                .Zip(EditDirectionalGains, (key, row) => (key, row.Gain))
                .ToDictionary(x => x.key, x => x.Gain),
            SpeedGains = _speedKeys
                .Zip(EditSpeedGains, (key, row) => (key, row.Gain))
                .ToDictionary(x => x.key, x => x.Gain)
        };

        StatusMessage = null;
        var saved = false;

        await RunSafeAsync(async () =>
        {
            await _service.SaveEditedProfileAsync(gameId, edited, cancellationToken);
            saved = true;
            StatusMessage = $"Saved your edits to the {gameId} profile.";
        });

        if (saved)
        {
            DiscardEdit();
            _pendingSelectionGameId = gameId;
            await RefreshAsync(cancellationToken);
        }
    }

    public async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        if (!CanExport || _selected is null)
        {
            return;
        }

        var gameId = _selected.GameId;
        var path = _exportPath;

        StatusMessage = null;

        await RunSafeAsync(async () =>
        {
            var fullPath = await _service.ExportAsync(gameId, path, cancellationToken);
            StatusMessage = $"Exported the {gameId} profile to {fullPath}.";
        });
    }

    protected override void OnBusyChanged()
    {
        UpdateCommands();
    }

    private void Apply(IReadOnlyList<ApprovedProfile> profiles)
    {
        // The store returns every approved version, including manual edits.
        // The page shows only the newest approved profile per game.
        var latest = profiles
            .GroupBy(x => x.GameId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(x => x.ApprovedAtUtc).First())
            .OrderByDescending(x => x.ApprovedAtUtc)
            .ToList();

        var keepGameId = _pendingSelectionGameId ?? _selected?.GameId;
        _pendingSelectionGameId = null;

        Profiles.Clear();
        foreach (var approved in latest)
        {
            Profiles.Add(new ProfileItemViewModel(approved));
        }

        if (_isEditing)
        {
            DiscardEdit();
        }

        _selected = keepGameId is null
            ? null
            : Profiles.FirstOrDefault(x => x.GameId == keepGameId);

        ResetExportPath();
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsEmpty));
        UpdateCommands();
    }

    private void ResetExportPath()
    {
        ExportPath = _selected is null
            ? string.Empty
            : Path.Combine(_exportDirectory, _selected.Profile.ToFileIdentifier() + ".json");
    }

    private EditableGainViewModel Track(EditableGainViewModel editor)
    {
        editor.Changed += (_, _) => OnEditorChanged();
        return editor;
    }

    private void OnEditorChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(EditorIsValid));
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        BeginEditCommand.RaiseCanExecuteChanged();
        SaveEditCommand.RaiseCanExecuteChanged();
        DiscardEditCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
    }
}
