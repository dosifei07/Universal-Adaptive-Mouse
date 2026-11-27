using System.Collections.ObjectModel;
using UAM.Application;
using UAM.Core.Models;
using UAM.Desktop.Shared.Mvvm;
using UAM.Desktop.Shared.Navigation;

namespace UAM.Desktop.ViewModels;

/// <summary>
/// Recommendation review and the approval workflow.
///
/// Approval boundary, enforced here and in the service layer:
/// <list type="bullet">
///   <item>Loading, generating, selecting, or filtering never approves.</item>
///   <item>Approval is two explicit steps: begin, then confirm, for the same
///   recommendation. Changing the selection cancels the pending confirmation.</item>
///   <item>Only a recommendation whose domain record says
///   <see cref="StoredRecommendation.CanApprove"/> can reach the service.</item>
///   <item>The only approval path is <see cref="IOptimizationService.ApproveAsync"/>,
///   which runs <c>RecommendationApprover</c>. This class never builds an
///   <see cref="ApprovedProfile"/> itself.</item>
/// </list>
/// </summary>
public sealed class OptimizationViewModel : PageViewModelBase
{
    private sealed record OptimizationData(
        IReadOnlyList<StoredRecommendation> Recommendations,
        IReadOnlyList<string> Games);

    private readonly IOptimizationService _service;
    private readonly IAnalysisService _analysisService;

    private IReadOnlyList<RecommendationItemViewModel> _all = [];
    private RecommendationItemViewModel? _selected;
    private RecommendationStatus? _statusFilter;
    private string? _selectedGame;
    private string? _pendingSelectionId;
    private string? _approvalTargetId;
    private bool _isConfirmingApproval;
    private string? _rejectionNote;
    private string? _statusMessage;

    public OptimizationViewModel(
        IOptimizationService service,
        IAnalysisService analysisService)
        : base(PageKey.Optimization)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _analysisService = analysisService
            ?? throw new ArgumentNullException(nameof(analysisService));

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsBusy);
        GenerateCommand = new AsyncRelayCommand(() => GenerateAsync(), () => CanGenerate);
        BeginApprovalCommand = new RelayCommand(BeginApproval, () => CanBeginApproval);
        ConfirmApprovalCommand = new AsyncRelayCommand(
            () => ConfirmApprovalAsync(),
            () => CanConfirmApproval);
        CancelApprovalCommand = new RelayCommand(
            CancelApproval,
            () => IsConfirmingApproval);
        RejectCommand = new AsyncRelayCommand(() => RejectAsync(), () => CanReject);
        ViewProfilesCommand = new RelayCommand(() => RequestNavigation(PageKey.Profiles));
    }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Creates a new pending recommendation. It does not approve it.</summary>
    public AsyncRelayCommand GenerateCommand { get; }

    /// <summary>Step one of approval: asks the user to confirm.</summary>
    public RelayCommand BeginApprovalCommand { get; }

    /// <summary>Step two of approval: the only command that calls ApproveAsync.</summary>
    public AsyncRelayCommand ConfirmApprovalCommand { get; }

    public RelayCommand CancelApprovalCommand { get; }

    public AsyncRelayCommand RejectCommand { get; }

    public RelayCommand ViewProfilesCommand { get; }

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = [];

    public ObservableCollection<string> Games { get; } = [];

    public IReadOnlyList<RecommendationStatus> AvailableStatuses { get; } =
        Enum.GetValues<RecommendationStatus>();

    public RecommendationItemViewModel? SelectedRecommendation
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
            {
                return;
            }

            // A confirmation belongs to one recommendation only.
            CancelApproval();
            OnPropertyChanged(nameof(HasSelection));
            UpdateCommands();
        }
    }

    public bool HasSelection => _selected is not null;

    public RecommendationStatus? StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                UpdateCommands();
            }
        }
    }

    public bool IsConfirmingApproval
    {
        get => _isConfirmingApproval;
        private set
        {
            if (SetProperty(ref _isConfirmingApproval, value))
            {
                OnPropertyChanged(nameof(ApprovalPrompt));
                UpdateCommands();
            }
        }
    }

    public string ApprovalPrompt => _isConfirmingApproval && _selected is not null
        ? $"Approve the proposed gains for {_selected.GameId}? This creates an " +
          "approved profile that can be exported. Nothing is applied to a game " +
          "automatically."
        : string.Empty;

    public string? RejectionNote
    {
        get => _rejectionNote;
        set => SetProperty(ref _rejectionNote, value);
    }

    /// <summary>Result of the last successful action, for display.</summary>
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

    public bool NeedsTrainingData => HasLoaded && Games.Count == 0 && !HasError;

    public bool IsEmpty => HasLoaded && Recommendations.Count == 0 && !HasError;

    public bool CanGenerate => !IsBusy && Games.Count > 0;

    public bool CanBeginApproval =>
        !IsBusy && !_isConfirmingApproval && _selected is { CanApprove: true };

    public bool CanConfirmApproval =>
        !IsBusy &&
        _isConfirmingApproval &&
        _selected is { CanApprove: true } &&
        _selected.Id == _approvalTargetId;

    public bool CanReject => !IsBusy && _selected is { IsPending: true };

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return LoadLatestAsync(
            async token => new OptimizationData(
                await _service.GetRecommendationsAsync(null, token),
                await _analysisService.GetGamesAsync(token)),
            Apply,
            cancellationToken);
    }

    public async Task GenerateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanGenerate)
        {
            return;
        }

        StatusMessage = null;
        string? createdId = null;

        await RunSafeAsync(async () =>
        {
            var stored = await _service.GenerateAsync(_selectedGame, cancellationToken);
            createdId = stored.Id;

            StatusMessage = stored.CanApprove
                ? $"New recommendation for {stored.GameId} is waiting for your review."
                : $"No confident adjustment was found for {stored.GameId}. " +
                  "It was recorded for transparency and cannot be approved.";
        });

        if (createdId is not null)
        {
            _pendingSelectionId = createdId;
            await RefreshAsync(cancellationToken);
        }
    }

    public void BeginApproval()
    {
        if (!CanBeginApproval || _selected is null)
        {
            return;
        }

        _approvalTargetId = _selected.Id;
        IsConfirmingApproval = true;
    }

    public void CancelApproval()
    {
        _approvalTargetId = null;
        IsConfirmingApproval = false;
    }

    public async Task ConfirmApprovalAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        if (!CanConfirmApproval || _approvalTargetId is null)
        {
            // Not confirmed for this recommendation, or no longer approvable.
            CancelApproval();
            return;
        }

        string targetId = _approvalTargetId;

        // Clear first so a double activation cannot submit twice.
        CancelApproval();
        StatusMessage = null;

        var approved = false;

        await RunSafeAsync(async () =>
        {
            var result = await _service.ApproveAsync(targetId, cancellationToken);
            approved = true;

            StatusMessage =
                $"Approved the recommendation for {result.GameId}. The profile " +
                "is on Game Profiles; nothing was applied to a game.";
        });

        if (approved)
        {
            _pendingSelectionId = targetId;
            await RefreshAsync(cancellationToken);
        }
    }

    public async Task RejectAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReject || _selected is null)
        {
            return;
        }

        var targetId = _selected.Id;
        var note = _rejectionNote;

        CancelApproval();
        StatusMessage = null;

        var rejected = false;

        await RunSafeAsync(async () =>
        {
            var result = await _service.RejectAsync(targetId, note, cancellationToken);
            rejected = true;
            StatusMessage = $"Rejected the recommendation for {result.GameId}.";
        });

        if (rejected)
        {
            RejectionNote = null;
            _pendingSelectionId = targetId;
            await RefreshAsync(cancellationToken);
        }
    }

    protected override void OnBusyChanged()
    {
        UpdateCommands();
    }

    private void Apply(OptimizationData data)
    {
        _all = data.Recommendations
            .Select(x => new RecommendationItemViewModel(x))
            .ToList();

        Games.Clear();
        foreach (var game in data.Games)
        {
            Games.Add(game);
        }

        if (_selectedGame is null || !Games.Contains(_selectedGame))
        {
            _selectedGame = Games.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedGame));
        }

        ApplyFilter();
        OnPropertyChanged(nameof(NeedsTrainingData));
        UpdateCommands();
    }

    private void ApplyFilter()
    {
        var keepId = _pendingSelectionId ?? _selected?.Id;
        _pendingSelectionId = null;

        var visible = _statusFilter is null
            ? _all
            : _all.Where(x => x.Status == _statusFilter).ToList();

        Recommendations.Clear();
        foreach (var item in visible)
        {
            Recommendations.Add(item);
        }

        // Re-select by id so a refresh keeps the user's place. The setter
        // cancels any confirmation, which is the safe default after a reload.
        SelectedRecommendation = keepId is null
            ? null
            : Recommendations.FirstOrDefault(x => x.Id == keepId);

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void UpdateCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        GenerateCommand.RaiseCanExecuteChanged();
        BeginApprovalCommand.RaiseCanExecuteChanged();
        ConfirmApprovalCommand.RaiseCanExecuteChanged();
        CancelApprovalCommand.RaiseCanExecuteChanged();
        RejectCommand.RaiseCanExecuteChanged();
    }
}
